using System;
using System.Configuration;
using ServiceLib;
using Vydejna.Contracts;
using Vydejna.Domain;
using StructureMap;
using StructureMap.Configuration.DSL;
using StructureMap.Pipeline;

namespace Vydejna.Server
{
    public class Program
    {
        private ProcessManagerSimple _processes;
        private IBus _bus;

        private void Initialize()
        {
            ObjectFactory.Initialize(x =>
            {
                x.AddRegistry<CoreRegistry>();
                x.AddRegistry<PostgresStoreRegistry>();
                x.AddRegistry<MultiNodePostgresRegistry>();
                x.AddRegistry<VydejnaRegistry>();
            });

            _bus = ObjectFactory.GetInstance<IBus>();

            _processes = new ProcessManagerSimple();
            _processes.Subscribe(_bus);
            _processes.RegisterLocal("MainBus", ObjectFactory.GetNamedInstance<IProcessWorker>("MainBusProcess"));
            _processes.RegisterLocal("HttpServer", ObjectFactory.GetNamedInstance<IProcessWorker>("HttpServerProcess"));
            _processes.RegisterGlobal("ProcesDefiniceNaradi", ObjectFactory.GetNamedInstance<IProcessWorker>("ProcesDefiniceNaradi"), 0, 0);
            _processes.RegisterGlobal("SeznamNaradiProjection", ObjectFactory.GetNamedInstance<IProcessWorker>("SeznamNaradiProjection"), 0, 0);
        }

        private void Start()
        {
            _bus.Publish(new SystemEvents.SystemInit());
        }

        private void Stop()
        {
            _bus.Publish(new SystemEvents.SystemShutdown());
        }

        private void WaitForExit()
        {
        }

        public static void Main(string[] args)
        {
            var program = new Program();
            Console.WriteLine("Starting...");
            program.Initialize();
            program.Start();
            Console.WriteLine("Running... Press enter for exit.");
            Console.ReadLine();
            Console.WriteLine("Stopping...");
            program.Stop();
            program.WaitForExit();
        }
    }

    public class CoreRegistry : Registry
    {
        public CoreRegistry()
        {
            For<string>().Use(Guid.NewGuid().ToString("N")).Named("NodeName");
            For<QueuedBus>().Singleton().Use(() => new QueuedBus(new SubscriptionManager(), "MainBus"));
            For<IQueueExecution>().Use<QueuedExecutionWorker>();
            For<IBus>().Add<QueuedBus>();
            For<IProcessWorker>().Add<QueuedBusProcess>().Named("MainBusProcess");

            For<IHttpRouter>().Singleton().Add<HttpRouter>();
            For<IHttpAddRoute>().Singleton().Add<HttpRouter>();
            For<HttpRouterCommon>().Singleton();
            For<IProcessWorker>().Use<HttpServer>()
                .Ctor<string[]>("prefixes").Is(new[] { ConfigurationManager.AppSettings["prefix"] })
                .Named("HttpServerProcess");

            For<ITime>().Singleton().Use<RealTime>();
            For<IMetadataManager>().Singleton().Use(x => new MetadataManager(
                x.GetInstance<IDocumentStore>().SubFolder("Metadata"), 
                x.GetInstance<INodeLockManager>()));
            For<IEventStreaming>().Singleton().Use<EventStreaming>();
            For<IEventStreamingDeserialized>().Transient().Use<EventStreamingDeserialized>();
            /*
             * routing to read and write services
             */
        }
    }
    public class SingleNodeRegistry : Registry
    {
        public SingleNodeRegistry()
        {
            For<INodeLockManager>().Singleton().Use<NodeLockManagerNull>();
            For<INetworkBus>().Singleton().Use<NetworkBusInMemory>();
        }
    }
    public class MultiNodePostgresRegistry : Registry
    {
        public MultiNodePostgresRegistry()
        {
            For<INodeLockManager>().Singleton().Use(x => new NodeLockManagerDocument(
                x.GetInstance<IDocumentStore>().SubFolder("Locking"),
                x.GetInstance<string>("NodeName"),
                x.GetInstance<ITime>(),
                new NotifyChangePostgres(x.GetInstance<DatabasePostgres>(), x.GetInstance<IQueueExecution>(), "Locking")));
            For<INetworkBus>().Singleton().Use<NetworkBusPostgres>()
                .Ctor<string>("nodeId").Named("NodeName");
        }
    }
    public class MemoryStoreRegistry : Registry
    {
        public MemoryStoreRegistry()
        {
            For<IEventStore>().Singleton().Use<EventStoreInMemory>();
            For<IEventStoreWaitable>().Singleton().Use<EventStoreInMemory>();
            For<IDocumentStore>().Singleton().Use<DocumentStoreInMemory>();
            For<INotifyChange>().Transient().Use<NotifyChangeDirect>();
        }
    }
    public class PostgresStoreRegistry : Registry
    {
        public PostgresStoreRegistry()
        {
            For<DatabasePostgres>().Singleton().Use<DatabasePostgres>()
                .Ctor<string>("connectionString").Is(ConfigurationManager.AppSettings["database"]);
            For<IEventStore>().Singleton().Use<EventStorePostgres>();
            For<IEventStoreWaitable>().Singleton().Use<EventStorePostgres>();
            For<IDocumentStore>().Singleton().Use<DocumentStorePostgres>();
        }
    }
    public class VydejnaRegistry : Registry
    {
        public VydejnaRegistry()
        {
            For<IProcessWorker>().Singleton().Use(x =>
            {
                var proces = new EventProcessSimple(
                    x.GetInstance<IMetadataManager>().GetConsumer("ProcesDefiniceNaradi"),
                    x.GetInstance<IEventStreamingDeserialized>(),
                    x.GetInstance<ICommandSubscriptionManager>());
                var service = x.GetInstance<SeznamNaradiService>();
                var handler = new ProcesDefiniceNaradi(service, service, service);
                proces.Register<ZahajenaDefiniceNaradiEvent>(handler);
                proces.Register<ZahajenaAktivaceNaradiEvent>(handler);
                proces.Register<DefinovanoNaradiEvent>(handler);
                return proces;
            }).Named("ProcesDefiniceNaradi");

            For<IProcessWorker>().Singleton().Use(x =>
                {
                    var serializer = x.GetInstance<SeznamNaradiSerializer>();
                    var projekce = new SeznamNaradiProjection(serializer);
                    var locking = new NodeLock(x.GetInstance<INodeLockManager>(), "SeznamNaradi");
                    var store = x.GetInstance<IDocumentStore>().SubFolder("SeznamNaradi");
                    var dispatcher = new PureProjectionDispatcherDeduplication<SeznamNaradiData>(
                        new PureProjectionDispatcher<SeznamNaradiData>(), projekce);
                    var cache = new PureProjectionStateCache<SeznamNaradiData>(store, serializer);
                    var notificator = x.TryGetInstance<INotifyChange>();
                    if (notificator != null)
                        cache.SetupNotificator(notificator);
                    var proces = new PureProjectionProcess<SeznamNaradiData>(
                        "ProcesDefiniceNaradi", projekce, locking, cache,
                        dispatcher, x.GetInstance<IEventStreamingDeserialized>());
                    return proces;
                }).Named("ProcesDefiniceNaradi");

            For<SeznamNaradiRest>().Singleton();
            For<SeznamNaradiSerializer>().Singleton();
            For<IReadSeznamNaradi>().Use<SeznamNaradiReader>()
                .Ctor<IDocumentFolder>("store").Is(x => x.GetInstance<IDocumentStore>().SubFolder("SeznamNaradi"));
            For<IWriteSeznamNaradi>().Use<SeznamNaradiService>();
            For<INaradiRepository>().Use<NaradiRepository>()
                .Ctor<string>("prefix").Is("naradi");
            For<IUnikatnostNaradiRepository>().Use<UnikatnostNaradiRepository>()
                .Ctor<string>("prefix").Is("unikatnost_naradi");
            
        }
    }
}
