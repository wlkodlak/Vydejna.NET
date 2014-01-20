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
        private IHttpRouteCommonConfigurator _router;

        private void Initialize()
        {
            ObjectFactory.Initialize(x =>
            {
                x.AddRegistry<CoreRegistry>();
                x.AddRegistry<PostgresStoreRegistry>();
                x.AddRegistry(new MultiNodePostgresRegistry(Guid.NewGuid().ToString("N")));
                x.AddRegistry<VydejnaRegistry>();
            });

            _bus = ObjectFactory.GetInstance<IBus>();

            _processes = new ProcessManagerSimple();
            _processes.RegisterBus("MainBus", _bus, ObjectFactory.GetNamedInstance<IProcessWorker>("MainBusProcess"));
            _processes.RegisterLocal("HttpServer", ObjectFactory.GetNamedInstance<IProcessWorker>("HttpServerProcess"));
            _processes.RegisterGlobal("ProcesDefiniceNaradi", ObjectFactory.GetNamedInstance<IProcessWorker>("ProcesDefiniceNaradi"), 0, 0);
            _processes.RegisterGlobal("SeznamNaradiProjection", ObjectFactory.GetNamedInstance<IProcessWorker>("SeznamNaradiProjection"), 0, 0);

            _router = ObjectFactory.GetNamedInstance<IHttpRouteCommonConfigurator>("HttpConfigRoot");
            ObjectFactory.GetInstance<SeznamNaradiRest>().RegisterHttpHandlers(_router);
            _router.Commit();
        }

        private void Start()
        {
            _processes.Start();
        }

        private void Stop()
        {
            _processes.Stop();
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
            Console.WriteLine("Waiting for exit...");
            program.WaitForExit();
        }
    }

    public class CoreRegistry : Registry
    {
        public CoreRegistry()
        {
            For<QueuedBus>().Singleton().Use(() => new QueuedBus(new SubscriptionManager(), "MainBus")).Named("MainBus");
            For<IQueueExecution>().Singleton().Use<QueuedExecutionWorker>().Named("Executor");
            Forward<QueuedBus, IBus>();
            For<IProcessWorker>().Add<QueuedBusProcess>().Named("MainBusProcess");
            For<ISubscriptionManager>().AlwaysUnique().Add<SubscriptionManager>();
            For<ICommandSubscriptionManager>().AlwaysUnique().Add<CommandSubscriptionManager>();

            For<HttpRouter>().Singleton().Use<HttpRouter>().Named("HttpRouter");
            Forward<HttpRouter, IHttpRouter>();
            Forward<HttpRouter, IHttpAddRoute>();
            For<IHttpRouteCommonConfigurator>().Singleton()
                .Use(x => { 
                    IHttpRouteCommonConfigurator cfg = new HttpRouterCommon(x.GetInstance<IHttpAddRoute>());
                    cfg.WithSerializer(new HttpSerializerJson()).WithSerializer(new HttpSerializerXml()).WithPicker(new HttpSerializerPicker());
                    return cfg;
                }).Named("HttpConfigRoot");
            For<IHttpServerDispatcher>().Add<HttpServerDispatcher>().Named("HttpDispatcher");
            For<IProcessWorker>().Use<HttpServer>()
                .Ctor<string[]>("prefixes").Is(new[] { ConfigurationManager.AppSettings["prefix"] })
                .Named("HttpServerProcess");

            For<ITime>().Singleton().Use<RealTime>().Named("TimeService");
            For<IMetadataManager>().Singleton().Use(x => new MetadataManager(
                x.GetInstance<IDocumentStore>().SubFolder("Metadata"),
                x.GetInstance<INodeLockManager>())).Named("MetadataManager");
            For<IEventStreaming>().Singleton().Use<EventStreaming>().Named("EventStreamingRaw");
            For<IEventStreamingDeserialized>().AlwaysUnique().Use<EventStreamingDeserialized>();
        }
    }
    public class SingleNodeRegistry : Registry
    {
        public SingleNodeRegistry()
        {
            For<INodeLockManager>().Singleton().Use<NodeLockManagerNull>().Named("LockManager");
            For<INetworkBus>().Singleton().Use<NetworkBusInMemory>().Named("NetworkBus");
            For<INotifyChange>().Singleton().MissingNamedInstanceIs.ConstructedBy(
                x => new NotifyChangeDirect(x.GetInstance<IQueueExecution>()));
        }
    }
    public class MultiNodePostgresRegistry : Registry
    {
        public MultiNodePostgresRegistry(string nodeName)
        {
            For<INodeLockManager>().Singleton().Use(x => new NodeLockManagerDocument(
                x.GetInstance<IDocumentStore>().SubFolder("Locking"),
                nodeName,
                x.GetInstance<ITime>(),
                x.GetInstance<INotifyChange>("NotifyLocking"))).Named("LockManager");
            For<INetworkBus>().Singleton().Use<NetworkBusPostgres>().Named("NetworkBus")
                .Ctor<string>("nodeId").Is(nodeName);
            For<INotifyChange>().AddInstances(i =>
                {
                    i.ConstructedBy(x => new NotifyChangePostgres(x.GetInstance<DatabasePostgres>(), x.GetInstance<IQueueExecution>(), "Locking")).Named("NotifyLocking");
                    i.ConstructedBy(x => new NotifyChangePostgres(x.GetInstance<DatabasePostgres>(), x.GetInstance<IQueueExecution>(), "SeznamNaradi")).Named("NotifySeznamNaradi");
                });
        }
    }
    public class MemoryStoreRegistry : Registry
    {
        public MemoryStoreRegistry()
        {
            For<IEventStoreWaitable>().Singleton().Use<EventStoreInMemory>().Named("EventStore");
            Forward<IEventStoreWaitable, IEventStore>();
            For<IDocumentStore>().Singleton().Use<DocumentStoreInMemory>().Named("DocumentStore");
        }
    }
    public class PostgresStoreRegistry : Registry
    {
        public PostgresStoreRegistry()
        {
            For<DatabasePostgres>().Singleton().Use<DatabasePostgres>().Named("PrimaryPostgresDatabase")
                .Ctor<string>("connectionString").Is(ConfigurationManager.AppSettings["database"]);
            For<IEventStoreWaitable>().Singleton().Use<EventStorePostgres>().Named("EventStore");
            Forward<IEventStoreWaitable, IEventStore>();
            For<IDocumentStore>().Singleton().Use<DocumentStorePostgres>().Named("DocumentStore");
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
                    var notificator = x.TryGetInstance<INotifyChange>("NotifySeznamNaradi");
                    if (notificator != null)
                        cache.SetupNotificator(notificator);
                    var proces = new PureProjectionProcess<SeznamNaradiData>(
                        "SeznamNaradiProjection", projekce, locking, cache,
                        dispatcher, x.GetInstance<IEventStreamingDeserialized>());
                    return proces;
                }).Named("SeznamNaradiProjection");

            For<SeznamNaradiSerializer>().Singleton().Use<SeznamNaradiSerializer>().Named("SeznamNaradiSerializer");
            For<IReadSeznamNaradi>().Singleton().Use<SeznamNaradiReader>().Named("ViewServiceSeznamNaradi")
                .Ctor<IDocumentFolder>("store").Is(x => x.GetInstance<IDocumentStore>().SubFolder("SeznamNaradi"))
                .Ctor<INotifyChange>("notifier").Is(x => x.GetInstance<INotifyChange>("NotifySeznamNaradi"));

            For<IWriteSeznamNaradi>().Use<SeznamNaradiService>().Named("DomainServiceSeznamNaradi");
            For<INaradiRepository>().Use<NaradiRepository>().Named("RepositoryNaradi")
                .Ctor<string>("prefix").Is("naradi");
            For<IUnikatnostNaradiRepository>().Use<UnikatnostNaradiRepository>().Named("RepositoryUnikatnost")
                .Ctor<string>("prefix").Is("unikatnost_naradi");

            For<SeznamNaradiRest>().Singleton().Use<SeznamNaradiRest>().Named("RestServiceSeznamNaradi");
            For<IEventSourcedSerializer>().Add<EventSourcedJsonSerializer>().Named("EventSourcedSerializerPrimary");
            For<ITypeMapper>().Use<TypeMapper>().OnCreation(m => VydejnaTypeMapperConfigurator.Configure(m)).Named("TypeMapperPrimary");
        }
    }
}
