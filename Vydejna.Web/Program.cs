using ServiceLib;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web;
using Vydejna.Contracts;
using Vydejna.Domain;
using Vydejna.Domain.CislovaneNaradi;
using Vydejna.Domain.DefinovaneNaradi;
using Vydejna.Domain.ExterniCiselniky;
using Vydejna.Domain.Procesy;
using Vydejna.Domain.UnikatnostNaradi;
using Vydejna.Projections;
using Vydejna.Projections.DetailNaradiReadModel;
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

namespace Vydejna.Web
{
    public class Program
    {
        private string _postgresConnectionString, _nodeId, _multiNode;
        private IProcessManager _processes;
        private IHttpRouteCommonConfigurator _router;
        private EventStorePostgres _eventStore;
        private ITime _time;
        private TypeMapper _typeMapper;
        private MetadataManager _metadataManager;
        private EventStreaming _eventStreaming;
        private EventSourcedJsonSerializer _eventSerializer;
        private List<IDisposable> _disposables;
        private HttpHandler _handler;
        private QueuedBus _mainBus;

        public void Initialize()
        {
            _postgresConnectionString = ConfigurationManager.AppSettings.Get("database");
            _multiNode = ConfigurationManager.AppSettings.Get("multinode"); ;
            _nodeId = ConfigurationManager.AppSettings.Get("node"); ;
            if (string.IsNullOrEmpty(_nodeId))
                _nodeId = Guid.NewGuid().ToString();

            _time = new RealTime();
            _disposables = new List<IDisposable>();

            _mainBus = new QueuedBus(new SubscriptionManager(), "MainBus");
            var postgres = new DatabasePostgres(_postgresConnectionString, _time);

            if (_multiNode == "false")
                _processes = new ProcessManagerLocal(_time);
            else
            {
                var internodeMessaging = new ProcessManagerPublisher(postgres, _mainBus);
                _processes = new ProcessManagerCluster(_time, _nodeId, internodeMessaging);
            }
            _processes.RegisterBus("MainBus", _mainBus, new QueuedBusProcess(_mainBus));

            var httpRouter = new HttpRouter();
            _router = new HttpRouterCommon(httpRouter);
            _router.WithSerializer(new HttpSerializerJson()).WithSerializer(new HttpSerializerXml()).WithPicker(new HttpSerializerPicker());
            _handler = new HttpHandler(new HttpServerDispatcher(httpRouter));

            new DomainRestInterface(_mainBus).Register(_router);
            new ProjectionsRestInterface(_mainBus).Register(_router);
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
            new ExterniCiselnikyService(new ExterniCiselnikyRepository(_eventStore, "externi", _eventSerializer)).Subscribe(_mainBus);
            new UnikatnostNaradiService(new UnikatnostNaradiRepository(_eventStore, "unikatnost", _eventSerializer)).Subscribe(_mainBus);

            new DetailNaradiReader(new DetailNaradiRepository(documentStore.GetFolder("detailnaradi")), _time).Subscribe(_mainBus);
            new IndexObjednavekReader(new IndexObjednavekRepository(documentStore.GetFolder("indexobjednavek")), _time).Subscribe(_mainBus);
            new NaradiNaObjednavceReader(new NaradiNaObjednavceRepository(documentStore.GetFolder("naradiobjednavky")), _time).Subscribe(_mainBus);
            new NaradiNaPracovistiReader(new NaradiNaPracovistiRepository(documentStore.GetFolder("naradipracoviste")), _time).Subscribe(_mainBus);
            new NaradiNaVydejneReader(new NaradiNaVydejneRepository(documentStore.GetFolder("naradyvydejny")), _time).Subscribe(_mainBus);
            new PrehledAktivnihoNaradiReader(new PrehledAktivnihoNaradiRepository(documentStore.GetFolder("prehlednaradi")), _time).Subscribe(_mainBus);
            new PrehledCislovanehoNaradiReader(new PrehledCislovanehoNaradiRepository(documentStore.GetFolder("prehledcislovanych")), _time).Subscribe(_mainBus);
            new PrehledObjednavekReader(new PrehledObjednavekRepository(documentStore.GetFolder("prehledobjednavek")), _time).Subscribe(_mainBus);
            new SeznamDodavateluReader(new SeznamDodavateluRepository(documentStore.GetFolder("seznamdodavatelu")), _time).Subscribe(_mainBus);
            new SeznamNaradiReader(new SeznamNaradiRepository(documentStore.GetFolder("seznamnaradi")), _time).Subscribe(_mainBus);
            new SeznamPracovistReader(new SeznamPracovistRepository(documentStore.GetFolder("seznampracovist")), _time).Subscribe(_mainBus);
            new SeznamVadReader(new SeznamVadRepository(documentStore.GetFolder("seznamvad")), _time).Subscribe(_mainBus);

            BuildEventProcessor(new ProcesDefiniceNaradi(_mainBus), "ProcesDefiniceNaradi");

            BuildProjection(new DetailNaradiProjection(new DetailNaradiRepository(documentStore.GetFolder("detailnaradi")), _time), "DetailNaradi");
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

        public void OnBeginRequest(HttpContext nativeContext)
        {
            nativeContext.RemapHandler(_handler);
        }

        private void BuildEventProcessor<T>(T processor, string processorName)
            where T : ISubscribeToCommandManager
        {
            var subscriptions = new CommandSubscriptionManager();
            processor.Subscribe(subscriptions);
            var process = new EventProcessSimple(_metadataManager.GetConsumer(processorName),
                new EventStreamingDeserialized(_eventStreaming, _eventSerializer), subscriptions, _time);
            _processes.RegisterGlobal("ProcesDefiniceNaradi", process, 0, 0);
        }

        private void BuildProjection<T>(T projection, string projectionName)
            where T : IEventProjection, ISubscribeToCommandManager
        {
            var subscriptions = new CommandSubscriptionManager();
            projection.Subscribe(subscriptions);
            var projector = new EventProjectorSimple(projection, _metadataManager.GetConsumer(projectionName),
                new EventStreamingDeserialized(_eventStreaming, _eventSerializer), subscriptions, _time);
            _processes.RegisterGlobal(projectionName, projector, 0, 0);
        }

        public void Start()
        {
            _processes.Start();
        }

        public void Stop()
        {
            _processes.Stop();
            _disposables.ForEach(x => x.Dispose());
        }

        public void WaitForExit()
        {
            _processes.WaitForStop();
        }
    }
}