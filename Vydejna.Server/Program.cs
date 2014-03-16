using System;
using System.Configuration;
using ServiceLib;
using Vydejna.Contracts;
using Vydejna.Domain;
using StructureMap;
using StructureMap.Configuration.DSL;
using StructureMap.Pipeline;
using Vydejna.Domain.DefinovaneNaradi;
using Vydejna.Domain.UnikatnostNaradi;
using Vydejna.Domain.Procesy;
using Vydejna.Projections.SeznamNaradiReadModel;

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
            ObjectFactory.AssertConfigurationIsValid();

            _bus = ObjectFactory.GetInstance<IBus>();

            _processes = new ProcessManagerSimple();
            _processes.RegisterBus("MainBus", _bus, ObjectFactory.GetNamedInstance<IProcessWorker>("MainBusProcess"));
            _processes.RegisterLocal("HttpServer", ObjectFactory.GetNamedInstance<IProcessWorker>("HttpServerProcess"));
            _processes.RegisterGlobal("ProcesDefiniceNaradi", ObjectFactory.GetNamedInstance<IProcessWorker>("ProcesDefiniceNaradi"), 0, 0);
            _processes.RegisterGlobal("SeznamNaradiProjection", ObjectFactory.GetNamedInstance<IProcessWorker>("SeznamNaradiProjection"), 0, 0);

            _router = ObjectFactory.GetNamedInstance<IHttpRouteCommonConfigurator>("HttpConfigRoot");
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
            _processes.WaitForStop();
            ObjectFactory.Container.Dispose();
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
            Console.WriteLine("Processes stopped.");
        }
    }

    public class CoreRegistry : Registry
    {
        public CoreRegistry()
        {
            For<QueuedBus>().Singleton().Use(() => new QueuedBus(new SubscriptionManager(), "MainBus")).Named("MainBus");
            Forward<QueuedBus, IBus>();
            For<IQueueExecution>().Singleton().Use(x =>
            {
                var bus = x.GetInstance<IBus>();
                var worker = new QueuedExecutionWorker(bus);
                worker.Subscribe(bus);
                return worker;
            }).Named("Executor");
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
                x.GetInstance<IDocumentStore>().GetFolder("Metadata"),
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
                x.GetInstance<IDocumentStore>().GetFolder("Locking"),
                nodeName,
                x.GetInstance<ITime>(),
                x.GetInstance<INotifyChange>("NotifyLocking"))).Named("LockManager");
            For<INetworkBus>().Singleton().Use<NetworkBusPostgres>().Named("NetworkBus")
                .Ctor<string>("nodeId").Is(nodeName)
                .OnCreation(n => n.Initialize());
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
            For<IEventStoreWaitable>().Singleton().Use<EventStorePostgres>().Named("EventStore").OnCreation(e => e.Initialize());
            Forward<IEventStoreWaitable, IEventStore>();
            For<IDocumentStore>().Singleton().Use<DocumentStorePostgres>().Named("DocumentStore")
                .Ctor<string>("partition").Is("documents")
                .OnCreation(e => e.Initialize());
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
                var svcDefinovane = x.GetInstance<DefinovaneNaradiService>();
                var svcUnikatnost = x.GetInstance<UnikatnostNaradiService>();
                var handler = new ProcesDefiniceNaradi(svcUnikatnost, svcDefinovane, svcUnikatnost);
                proces.Register<ZahajenaDefiniceNaradiEvent>(handler);
                proces.Register<ZahajenaAktivaceNaradiEvent>(handler);
                proces.Register<DefinovanoNaradiEvent>(handler);
                return proces;
            }).Named("ProcesDefiniceNaradi");

            For<IProcessWorker>().Singleton().Use(x =>
                {
                    var folder = x.GetInstance<IDocumentStore>().GetFolder("SeznamNaradi");
                    var executor = x.GetInstance<IQueueExecution>();
                    var time = x.GetInstance<ITime>();
                    var repository = new SeznamNaradiRepository(folder);
                    var projekce = new SeznamNaradiProjection(repository, executor, time);
                    var metadata = x.GetInstance<IMetadataManager>().GetConsumer("SeznamNaradi");
                    var streaming = x.GetInstance<IEventStreamingDeserialized>();
                    var rawSubscriptions = new CommandSubscriptionManager();
                    var subscriptions = new QueuedCommandSubscriptionManager(rawSubscriptions, executor);
                    projekce.Subscribe(subscriptions);
                    var proces = new EventProjectorSimple(projekce, metadata, streaming, subscriptions);
                    return proces;
                }).Named("SeznamNaradiProjection");

            For<SeznamNaradiReader>().Singleton().Use<SeznamNaradiReader>().Named("ViewServiceSeznamNaradi")
                .Ctor<IDocumentFolder>("store").Is(x => x.GetInstance<IDocumentStore>().GetFolder("SeznamNaradi"))
                .Ctor<INotifyChange>("notifier").Is(x => x.GetInstance<INotifyChange>("NotifySeznamNaradi"));
            Forward<SeznamNaradiReader, IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>>();
            Forward<SeznamNaradiReader, IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>>();

            For<DefinovaneNaradiService>().Singleton().Use<DefinovaneNaradiService>().Named("DefinovaneNaradiDomainService");
            Forward<DefinovaneNaradiService, IHandle<CommandExecution<AktivovatNaradiCommand>>>();
            Forward<DefinovaneNaradiService, IHandle<CommandExecution<DeaktivovatNaradiCommand>>>();
            Forward<DefinovaneNaradiService, IHandle<CommandExecution<DefinovatNaradiInternalCommand>>>();
            For<IDefinovaneNaradiRepository>().Use<DefinovaneNaradiRepository>().Named("RepositoryNaradi")
                .Ctor<string>("prefix").Is("naradi");

            For<UnikatnostNaradiService>().Singleton().Use<UnikatnostNaradiService>().Named("UnikatnostNaradiDomainService");
            Forward<UnikatnostNaradiService, IHandle<CommandExecution<DefinovatNaradiCommand>>>();
            Forward<UnikatnostNaradiService, IHandle<CommandExecution<DokoncitDefiniciNaradiInternalCommand>>>();
            For<IUnikatnostNaradiRepository>().Use<UnikatnostNaradiRepository>().Named("RepositoryUnikatnost")
                .Ctor<string>("prefix").Is("unikatnost_naradi");

            For<IEventSourcedSerializer>().Add<EventSourcedJsonSerializer>().Named("EventSourcedSerializerPrimary");
            For<IRegisterTypes>().AddInstances(b => {
                b.Type<SeznamNaradiTypeMapping>();
                b.Type<ExterniCiselnikyTypeMapping>();
                b.Type<CislovaneNaradiTypeMapping>();
                b.Type<NecislovaneNaradiTypeMapping>();
                b.Type<ObecneNaradiTypeMapping>();
            });
            For<ITypeMapper>().Use(ctx =>
            {
                var mapper = new TypeMapper();
                foreach (var registrator in ctx.GetAllInstances<IRegisterTypes>())
                    registrator.Register(mapper);
                return mapper;
            }).Named("TypeMapperPrimary");
        }
    }
}
