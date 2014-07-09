using ServiceLib;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading;
using Vydejna.Contracts;
using Vydejna.Domain;
using Vydejna.Domain.CislovaneNaradi;
using Vydejna.Domain.DefinovaneNaradi;
using Vydejna.Domain.ExterniCiselniky;
using Vydejna.Domain.NecislovaneNaradi;
using Vydejna.Domain.Procesy;
using Vydejna.Domain.UnikatnostNaradi;
using Vydejna.Projections;
using Vydejna.Projections.DetailNaradiReadModel;
using Vydejna.Projections.HistorieNaradiReadModel;
using Vydejna.Projections.IndexObjednavekReadModel;
using Vydejna.Projections.NaradiNaObjednavceReadModel;
using Vydejna.Projections.NaradiNaPracovistiReadModel;
using Vydejna.Projections.NaradiNaVydejneReadModel;
using Vydejna.Projections.PrehledAktivnihoNaradiReadModel;
using Vydejna.Projections.PrehledCislovanehoNaradiReadModel;
using Vydejna.Projections.PrehledObjednavekReadModel;
using Vydejna.Projections.SeznamDodavateluReadModel;
using Vydejna.Projections.SeznamNaradiReadModel;
using Vydejna.Projections.SeznamPracovistReadModel;
using Vydejna.Projections.SeznamVadReadModel;

namespace Vydejna.Server
{
    public class Program
    {
        private string _postgresConnectionString, _nodeId, _multiNode;
        private IList<string> _httpPrefixes;
        private IProcessManager _processes;
        private IHttpRouteCommonConfigurator _router;
        private EventStorePostgres _eventStore;
        private ITime _time;
        private TypeMapper _typeMapper;
        private MetadataManager _metadataManager;
        private EventStreaming _eventStreaming;
        private EventSourcedJsonSerializer _eventSerializer;
        private List<IDisposable> _disposables;
        private bool _running;
        private Dictionary<string, CommandInfo> _consoleCommands;
        private QueuedBus _mainBus;
        private ThreadedTaskScheduler _scheduler;
        private EventProcessTracking _trackingCoordinator;

        public Program()
        {
            _running = true;
            _consoleCommands = new Dictionary<string, CommandInfo>();
        }

        private class CommandInfo
        {
            public string Name { get; private set; }
            public string Help { get; private set; }
            private int _parameters;
            private Action<string[]> _handler;

            public CommandInfo(string name, string help, int parameters, Action<string[]> handler)
            {
                Name = name;
                Help = help;
                _parameters = parameters;
                _handler = handler;
            }
            protected CommandInfo(string name, string help)
            {
                Name = name;
                Help = help;
            }
            public virtual void Execute(string[] commandLine)
            {
                if (_handler == null)
                    Console.WriteLine("Command has no handler");
                else if (_parameters == -1 || _parameters + 1 == commandLine.Length)
                {
                    try
                    {
                        _handler(commandLine);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Command failed: {0}", ex.ToString());
                    }
                }
                else
                    Console.WriteLine("Command {0} requires {1} parameters", Name, _parameters);
            }
        }

        private class CommandInfoList : CommandInfo
        {
            private Program _parent;
            public CommandInfoList(Program parent)
                : base("list", "List processes")
            {
                _parent = parent;
            }
            public override void Execute(string[] commandLine)
            {
                if (commandLine.Length != 2)
                    Console.WriteLine("Command list requires parameter: local | global | leader");
                else if (commandLine[1] == "local")
                {
                    var processes = _parent._processes.GetLocalProcesses();
                    Console.WriteLine("{0,-25} {1,-15}", "Process name", "Status");
                    foreach (var process in processes)
                        Console.WriteLine("{0,-25} {1,-15}", process.ProcessName, process.ProcessStatus);
                }
                else if (commandLine[1] == "leader")
                {
                    var processes = _parent._processes.GetLeaderProcesses();
                    Console.WriteLine("{0,-25} {1,-15} {2,-36}", "Process name", "Status", "Assigned node");
                    foreach (var process in processes)
                        Console.WriteLine("{0,-25} {1,-15} {2,-36}", process.ProcessName, process.ProcessStatus, process.AssignedNode);
                }
                else if (commandLine[1] == "global")
                {
                    var processes = _parent._processes.GetGlobalInfo();
                    Console.WriteLine("Node ID  : {0}", processes.NodeName);
                    Console.WriteLine("Leader   : {0}", processes.LeaderName);
                    Console.WriteLine("Connected: {0}", processes.IsConnected ? "yes" : "no");
                    Console.WriteLine("{0,-25} {1,-15}", "Process name", "Status");
                    foreach (var process in processes.RunningProcesses)
                        Console.WriteLine("{0,-25} {1,-15}", process.ProcessName, process.ProcessStatus);
                }
            }
        }

        private void Initialize()
        {
            _postgresConnectionString = ConfigurationManager.AppSettings.Get("database");
            _multiNode = ConfigurationManager.AppSettings.Get("multinode"); ;
            _nodeId = ConfigurationManager.AppSettings.Get("node"); ;
            if (string.IsNullOrEmpty(_nodeId))
                _nodeId = Guid.NewGuid().ToString();
            _httpPrefixes = new[] { ConfigurationManager.AppSettings.Get("prefix") };

            _time = new RealTime();
            _disposables = new List<IDisposable>();
            _scheduler = new ThreadedTaskScheduler();

            _mainBus = new QueuedBus(new SubscriptionManager(), "MainBus");
            var postgres = new DatabasePostgres(_postgresConnectionString, _time);
            postgres.UseScheduler(_scheduler);
            _disposables.Add(postgres);

            if (_multiNode == "false")
                _processes = new ProcessManagerLocal(_time);
            else
            {
                var internodeMessaging = new ProcessManagerPublisher(postgres, _mainBus);
                _processes = new ProcessManagerCluster(_time, _nodeId, internodeMessaging);
                _disposables.Add(internodeMessaging);
            }
            _processes.RegisterBus("MainBus", _mainBus, new QueuedBusProcess(_mainBus).WithWorkers(1));

            _trackingCoordinator = new EventProcessTracking(_time);
            _processes.RegisterLocal("EventHandlerTracking", _trackingCoordinator);

            var httpRouter = new HttpRouter();
            _router = new HttpRouterCommon(httpRouter);
            _router.WithSerializer(new HttpSerializerJson()).WithSerializer(new HttpSerializerXml()).WithPicker(new HttpSerializerPicker());
            _processes.RegisterLocal("HttpServer", new HttpServer(_httpPrefixes, new HttpServerDispatcher(httpRouter)).SetupWorkerCount(1));
            new DomainRestInterface(_mainBus).Register(_router);
            new ProjectionsRestInterface(_mainBus).Register(_router);
            new EventProcessTrackService(_trackingCoordinator).Register(_router);
            _router.Commit();

            var networkBus = new NetworkBusPostgres(_nodeId, postgres, _time);
            networkBus.Initialize();
            _disposables.Add(networkBus);

            _eventStore = new EventStorePostgres(postgres, _time);
            _eventStore.Initialize();
            _eventStreaming = new EventStreaming(_eventStore, networkBus);
            _disposables.Add(_eventStore);

            var documentStore = new DocumentStorePostgres(postgres, "documents");
            documentStore.Initialize();
            _disposables.Add(documentStore);

            _metadataManager = new MetadataManager(documentStore.GetFolder("metadata"));

            _typeMapper = new TypeMapper();
            _typeMapper.Register(new CislovaneNaradiTypeMapping());
            _typeMapper.Register(new ExterniCiselnikyTypeMapping());
            _typeMapper.Register(new NaradiNaUmisteniTypeMapping());
            _typeMapper.Register(new NecislovaneNaradiTypeMapping());
            _typeMapper.Register(new ObecneNaradiTypeMapping());
            _typeMapper.Register(new PrehledNaradiTypeMapping());
            _typeMapper.Register(new SeznamNaradiTypeMapping());
            _eventSerializer = new EventSourcedJsonSerializer(_typeMapper);

            new DefinovaneNaradiService(new DefinovaneNaradiRepository(_eventStore, "definovane", _eventSerializer)).Subscribe(_mainBus);
            new CislovaneNaradiService(new CislovaneNaradiRepository(_eventStore, "cislovane", _eventSerializer), _time).Subscribe(_mainBus);
            new NecislovaneNaradiService(new NecislovaneNaradiRepository(_eventStore, "necislovane", _eventSerializer), _time).Subscribe(_mainBus);
            new ExterniCiselnikyService(new ExternalEventRepository(_eventStore, "externi-", _eventSerializer)).Subscribe(_mainBus);
            new UnikatnostNaradiService(new UnikatnostNaradiRepository(_eventStore, "unikatnost", _eventSerializer)).Subscribe(_mainBus);

            new DetailNaradiReader(new DetailNaradiRepository(documentStore.GetFolder("detailnaradi")), _time).Subscribe(_mainBus);
            new HistorieNaradiReader(new HistorieNaradiRepositoryOperace(postgres)).Subscribe(_mainBus);
            new IndexObjednavekReader(new IndexObjednavekRepository(documentStore.GetFolder("indexobjednavek")),  _time).Subscribe(_mainBus);
            new NaradiNaObjednavceReader(new NaradiNaObjednavceRepository(documentStore.GetFolder("naradiobjednavky")),  _time).Subscribe(_mainBus);
            new NaradiNaPracovistiReader(new NaradiNaPracovistiRepository(documentStore.GetFolder("naradipracoviste")),  _time).Subscribe(_mainBus);
            new NaradiNaVydejneReader(new NaradiNaVydejneRepository(documentStore.GetFolder("naradyvydejny")),  _time).Subscribe(_mainBus);
            new PrehledAktivnihoNaradiReader(new PrehledAktivnihoNaradiRepository(documentStore.GetFolder("prehlednaradi")),  _time).Subscribe(_mainBus);
            new PrehledCislovanehoNaradiReader(new PrehledCislovanehoNaradiRepository(documentStore.GetFolder("prehledcislovanych")),  _time).Subscribe(_mainBus);
            new PrehledObjednavekReader(new PrehledObjednavekRepository(documentStore.GetFolder("prehledobjednavek")),  _time).Subscribe(_mainBus);
            new SeznamDodavateluReader(new SeznamDodavateluRepository(documentStore.GetFolder("seznamdodavatelu")),  _time).Subscribe(_mainBus);
            new SeznamNaradiReader(new SeznamNaradiRepository(documentStore.GetFolder("seznamnaradi")),  _time).Subscribe(_mainBus);
            new SeznamPracovistReader(new SeznamPracovistRepository(documentStore.GetFolder("seznampracovist")),  _time).Subscribe(_mainBus);
            new SeznamVadReader(new SeznamVadRepository(documentStore.GetFolder("seznamvad")),  _time).Subscribe(_mainBus);

            BuildEventProcessor(new ProcesDefiniceNaradi(_mainBus), "ProcesDefiniceNaradi");

            BuildProjection(new DetailNaradiProjection(new DetailNaradiRepository(documentStore.GetFolder("detailnaradi")),  _time), "DetailNaradi");
            BuildProjection(new HistorieNaradiProjection(
                new HistorieNaradiRepositoryOperace(postgres), 
                new HistorieNaradiRepositoryPomocne(documentStore.GetFolder("historienaradi")), _time), 
                "HistorieNaradi");
            BuildProjection(new IndexObjednavekProjection(new IndexObjednavekRepository(documentStore.GetFolder("indexobjednavek")), _time), "IndexObjednavek");
            BuildProjection(new NaradiNaObjednavceProjection(new NaradiNaObjednavceRepository(documentStore.GetFolder("naradiobjednavky")), _time), "NaradiNaObjednavce");
            BuildProjection(new NaradiNaPracovistiProjection(new NaradiNaPracovistiRepository(documentStore.GetFolder("naradipracoviste")), _time), "NaradiNaPracovisti");
            BuildProjection(new NaradiNaVydejneProjection(new NaradiNaVydejneRepository(documentStore.GetFolder("naradyvydejny")), _time), "NaradiNaVydejne");
            BuildProjection(new PrehledAktivnihoNaradiProjection(new PrehledAktivnihoNaradiRepository(documentStore.GetFolder("prehlednaradi")), _time), "PrehledAktivnihoNaradi");
            BuildProjection(new PrehledCislovanehoNaradiProjection(new PrehledCislovanehoNaradiRepository(documentStore.GetFolder("prehledcislovanych")), _time), "PrehledCislovanehoNaradi");
            BuildProjection(new PrehledObjednavekProjection(new PrehledObjednavekRepository(documentStore.GetFolder("prehledobjednavek")), _time), "PrehledObjednavek");
            BuildProjection(new SeznamDodavateluProjection(new SeznamDodavateluRepository(documentStore.GetFolder("seznamdodavatelu")), _time), "SeznamDodavatelu");
            BuildProjection(new SeznamNaradiProjection(new SeznamNaradiRepository(documentStore.GetFolder("seznamnaradi")), _time), "SeznamNaradi");
            BuildProjection(new SeznamPracovistProjection(new SeznamPracovistRepository(documentStore.GetFolder("seznampracovist")), _time), "SeznamPracovist");
            BuildProjection(new SeznamVadProjection(new SeznamVadRepository(documentStore.GetFolder("seznamvad")), _time), "SeznamVad");
        }

        private void BuildEventProcessor<T>(T processor, string processorName)
            where T : ISubscribeToEventManager
        {
            var subscriptions = new EventSubscriptionManager();
            processor.Subscribe(subscriptions);
            var process = new EventProcessSimple(
                _metadataManager.GetConsumer(processorName),
                new EventStreamingDeserialized(_eventStreaming, _eventSerializer), 
                subscriptions, _time,
                _trackingCoordinator.RegisterHandler(processorName));
            _processes.RegisterGlobal(processorName, process, 0, 0);
        }

        private void BuildProjection<T>(T projection, string projectionName)
            where T : IEventProjection, ISubscribeToEventManager
        {
            var subscriptions = new EventSubscriptionManager();
            projection.Subscribe(subscriptions);
            var projector = new EventProjectorSimple(
                projection, 
                _metadataManager.GetConsumer(projectionName),
                new EventStreamingDeserialized(_eventStreaming, _eventSerializer), 
                subscriptions, 
                _time,
                _trackingCoordinator.RegisterHandler(projectionName));
            _processes.RegisterGlobal(projectionName, projector, 0, 0);
        }

        private void Start()
        {
            _processes.Start();
        }

        private void Stop()
        {
            _processes.Stop();
            _disposables.ForEach(x => x.Dispose());
        }

        private void WaitForExit()
        {
            _processes.WaitForStop();
        }

        private void RegisterCommands()
        {
            _consoleCommands["list"] = new CommandInfoList(this);
            _consoleCommands["exit"] = new CommandInfo("exit", "Stop whole server process", 0, cmd => _running = false);
            _consoleCommands["start"] = new CommandInfo("start", "Start process", 1,
                cmd => _mainBus.Publish(new ProcessManagerMessages.ProcessRequest { ProcessName = cmd[1], ShouldBeOnline = true }));
            _consoleCommands["stop"] = new CommandInfo("stop", "Stop process", 1,
                cmd => _mainBus.Publish(new ProcessManagerMessages.ProcessRequest { ProcessName = cmd[1], ShouldBeOnline = false }));
            _consoleCommands["help"] = new CommandInfo("help", "Show this help", 0, cmd =>
            {
                foreach (var command in _consoleCommands.Values)
                    Console.WriteLine("{0}: {1}", command.Name, command.Help);
            });
        }

        private void ExecuteCommand(string[] commandLine)
        {
            if (commandLine.Length == 0)
                return;
            CommandInfo command;
            if (!_consoleCommands.TryGetValue(commandLine[0].ToLowerInvariant(), out command))
                Console.WriteLine("Unknown command {0}", commandLine[0]);
            else
                command.Execute(commandLine);
        }

        public static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            var program = new Program();
            Console.WriteLine("Starting...");
            program.Initialize();
            program.RegisterCommands();
            program.Start();
            var consoleThread = Thread.CurrentThread;
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; program._running = false; consoleThread.Interrupt(); };
            while (program._running)
            {
                try
                {
                    Console.Write("> ");
                    var commandLine = Console.ReadLine();
                    if (string.IsNullOrEmpty(commandLine))
                        continue;
                    var parsedCommand = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    program.ExecuteCommand(parsedCommand);
                }
                catch (ThreadInterruptedException)
                {
                    program._running = false;
                }
            }
            Console.WriteLine("Stopping...");
            program.Stop();
            Console.WriteLine("Waiting for exit...");
            program.WaitForExit();
            Console.WriteLine("Processes stopped.");
            log4net.LogManager.Shutdown();
        }
    }
}