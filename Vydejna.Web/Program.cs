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
        private string _postgresConnectionString, _nodeId;
        private IList<string> _httpPrefixes;
        private ProcessManagerSimple _processes;
        private IHttpRouteCommonConfigurator _router;
        private EventStorePostgres _eventStore;
        private ITime _time;
        private TypeMapper _typeMapper;
        private MetadataManager _metadataManager;
        private EventStreaming _eventStreaming;
        private EventSourcedJsonSerializer _eventSerializer;
        private List<IDisposable> _disposables;
        private HttpHandler _handler;

        public void Initialize()
        {
            _postgresConnectionString = ConfigurationManager.AppSettings.Get("database");
            _nodeId = ConfigurationManager.AppSettings.Get("node"); ;

            _time = new RealTime();
            _processes = new ProcessManagerSimple(_time);
            _disposables = new List<IDisposable>();

            var mainBus = new QueuedBus(new SubscriptionManager(), "MainBus");
            _processes.RegisterBus("MainBus", mainBus, new QueuedBusProcess(mainBus));

            var httpRouter = new HttpRouter();
            _router = new HttpRouterCommon(httpRouter);
            _router.WithSerializer(new HttpSerializerJson()).WithSerializer(new HttpSerializerXml()).WithPicker(new HttpSerializerPicker());
            _handler = new HttpHandler(new HttpServerDispatcher(httpRouter));

            new DomainRestInterface(mainBus).Register(_router);
            new ProjectionsRestInterface(mainBus).Register(_router);
            _router.Commit();

            var postgres = new DatabasePostgres(_postgresConnectionString, _time);
            _eventStore = new EventStorePostgres(postgres, _time);
            _eventStore.Initialize();
            var networkBus = new NetworkBusPostgres(_nodeId, postgres, _time);
            networkBus.Initialize();
            var documentStore = new DocumentStorePostgres(postgres, "documents");
            documentStore.Initialize();
            _metadataManager = new MetadataManager(documentStore.GetFolder("metadata"));
            _eventStreaming = new EventStreaming(_eventStore, networkBus);

            _disposables.Add(postgres);
            _disposables.Add(documentStore);
            _disposables.Add(_eventStore);

            _typeMapper = new TypeMapper();
            _typeMapper.Register(new CislovaneNaradiTypeMapping());
            _typeMapper.Register(new ExterniCiselnikyTypeMapping());
            _typeMapper.Register(new NaradiNaUmisteniTypeMapping());
            _typeMapper.Register(new NecislovaneNaradiTypeMapping());
            _typeMapper.Register(new ObecneNaradiTypeMapping());
            _typeMapper.Register(new PrehledNaradiTypeMapping());
            _typeMapper.Register(new SeznamNaradiTypeMapping());
            _eventSerializer = new EventSourcedJsonSerializer(_typeMapper);

            new DefinovaneNaradiService(new DefinovaneNaradiRepository(_eventStore, "definovane", _eventSerializer)).Subscribe(mainBus);
            new CislovaneNaradiService(new CislovaneNaradiRepository(_eventStore, "cislovane", _eventSerializer), _time).Subscribe(mainBus);
            new ExterniCiselnikyService(new ExterniCiselnikyRepository(_eventStore, "externi", _eventSerializer)).Subscribe(mainBus);
            new UnikatnostNaradiService(new UnikatnostNaradiRepository(_eventStore, "unikatnost", _eventSerializer)).Subscribe(mainBus);

            new DetailNaradiReader(new DetailNaradiRepository(documentStore.GetFolder("detailnaradi")), _time).Subscribe(mainBus);
            new IndexObjednavekReader(new IndexObjednavekRepository(documentStore.GetFolder("indexobjednavek")), _time).Subscribe(mainBus);
            new NaradiNaObjednavceReader(new NaradiNaObjednavceRepository(documentStore.GetFolder("naradiobjednavky")), _time).Subscribe(mainBus);
            new NaradiNaPracovistiReader(new NaradiNaPracovistiRepository(documentStore.GetFolder("naradipracoviste")), _time).Subscribe(mainBus);
            new NaradiNaVydejneReader(new NaradiNaVydejneRepository(documentStore.GetFolder("naradyvydejny")), _time).Subscribe(mainBus);
            new PrehledAktivnihoNaradiReader(new PrehledAktivnihoNaradiRepository(documentStore.GetFolder("prehlednaradi")), _time).Subscribe(mainBus);
            new PrehledCislovanehoNaradiReader(new PrehledCislovanehoNaradiRepository(documentStore.GetFolder("prehledcislovanych")), _time).Subscribe(mainBus);
            new PrehledObjednavekReader(new PrehledObjednavekRepository(documentStore.GetFolder("prehledobjednavek")), _time).Subscribe(mainBus);
            new SeznamDodavateluReader(new SeznamDodavateluRepository(documentStore.GetFolder("seznamdodavatelu")), _time).Subscribe(mainBus);
            new SeznamNaradiReader(new SeznamNaradiRepository(documentStore.GetFolder("seznamnaradi")), _time).Subscribe(mainBus);
            new SeznamPracovistReader(new SeznamPracovistRepository(documentStore.GetFolder("seznampracovist")), _time).Subscribe(mainBus);
            new SeznamVadReader(new SeznamVadRepository(documentStore.GetFolder("seznamvad")), _time).Subscribe(mainBus);

            BuildEventProcessor(new ProcesDefiniceNaradi(mainBus), "ProcesDefiniceNaradi");

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