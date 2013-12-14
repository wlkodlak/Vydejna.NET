using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;


namespace Vydejna.Domain
{
    public class Bootstrap
    {
        private IBus _bus;
        
        public void Init()
        {
            var time = new RealTime();
            var typeMapper = new TypeMapper();
            VydejnaContractList.RegisterTypes(typeMapper);
            var serializer = new EventSourcedJsonSerializer(typeMapper);
            _bus = new DirectBus(new SubscriptionManager());

            var sqlConfig = new SqlConfiguration(@"server=.\SQLEXPRESS");
            var documentStore = new DocumentStoreSql(sqlConfig);
            var eventStore = new EventStoreSql(sqlConfig);
            var eventStreaming = new EventStreamingIndividual(new EventStoreWaitable(eventStore, time), typeMapper);
            var metadataManager = new ProjectionMetadataManager(documentStore, "Metadata");

            var seznamNaradiProjectionMaster = new SeznamNaradiProjection(documentStore, "SeznamNaradi");
            var seznamNaradiProcessMaster = new ProjectionProcess(eventStreaming, metadataManager, serializer)
                .Setup(seznamNaradiProjectionMaster).AsMaster();
            seznamNaradiProcessMaster.Register<DefinovanoNaradiEvent>(seznamNaradiProjectionMaster);
            seznamNaradiProcessMaster.Register<AktivovanoNaradiEvent>(seznamNaradiProjectionMaster);
            seznamNaradiProcessMaster.Register<DeaktivovanoNaradiEvent>(seznamNaradiProjectionMaster);
            _bus.Subscribe<SystemEvents.SystemInit>(msg => seznamNaradiProcessMaster.Start());
            _bus.Subscribe<SystemEvents.SystemShutdown>(msg => seznamNaradiProcessMaster.Stop());

            var seznamNaradiProjectionRebuild = new SeznamNaradiProjection(documentStore, "SeznamNaradi");
            var seznamNaradiProcessRebuild = new ProjectionProcess(eventStreaming, metadataManager, serializer)
                .Setup(seznamNaradiProjectionRebuild).AsRebuilder();
            seznamNaradiProcessRebuild.Register<DefinovanoNaradiEvent>(seznamNaradiProjectionRebuild);
            seznamNaradiProcessRebuild.Register<AktivovanoNaradiEvent>(seznamNaradiProjectionRebuild);
            seznamNaradiProcessRebuild.Register<DeaktivovanoNaradiEvent>(seznamNaradiProjectionRebuild);
            _bus.Subscribe<SystemEvents.SystemInit>(msg => seznamNaradiProcessRebuild.Start());
            _bus.Subscribe<SystemEvents.SystemShutdown>(msg => seznamNaradiProcessRebuild.Stop());

            var seznamNaradiReader = new SeznamNaradiReader()
                .Register("A", seznamNaradiProjectionMaster)
                .Register("B", seznamNaradiProjectionRebuild);
            var seznamNaradiService = new SeznamNaradiService(
                    new NaradiRepository(eventStore, "Naradi", serializer),
                    new UnikatnostNaradiRepository(eventStore, "Unikatnost", serializer));

            var seznamNaradiProcess = new ProcesDefiniceNaradi(seznamNaradiService, seznamNaradiService, seznamNaradiService);
            var eventProcessor = new EventProcess(eventStreaming, "ProcesDefiniceNaradi");
            eventProcessor.Register<ZahajenaDefiniceNaradiEvent>(seznamNaradiProcess);
            eventProcessor.Register<ZahajenaAktivaceNaradiEvent>(seznamNaradiProcess);
            eventProcessor.Register<DefinovanoNaradiEvent>(seznamNaradiProcess);
            _bus.Subscribe<SystemEvents.SystemInit>(msg => eventProcessor.Start());
            _bus.Subscribe<SystemEvents.SystemShutdown>(msg => eventProcessor.Stop());

            var httpRouter = new HttpRouter();
            var httpServer = new HttpServer(new[] { "http://localhost/" }, new HttpServerDispatcher(httpRouter));
            _bus.Subscribe<SystemEvents.SystemInit>(msg => httpServer.Start());
            _bus.Subscribe<SystemEvents.SystemShutdown>(msg => httpServer.Stop());
            var httpRoutes = new HttpRouteConfig(httpRouter, HttpStagedHandler.CreateBuilder);
            var httpStandard = httpRoutes.Common()
                .With(new HttpUrlEnhancer())
                .With(new HttpOutputDirect());
            var seznamNaradiRest = new SeznamNaradiRest(
                seznamNaradiService,
                new SeznamNaradiProxy(metadataManager).Register(seznamNaradiReader));

            httpRoutes.Route("/SeznamNaradi").Using(httpStandard)
                .To(seznamNaradiRest.NacistSeznamNaradi)
                .With(new HttpOutputJson<ZiskatSeznamNaradiResponse>());
            httpRoutes.Route("/OveritUnikatnost").Using(httpStandard)
                .To(seznamNaradiRest.OveritUnikatnost)
                .With(new HttpOutputJson<OvereniUnikatnostiResponse>());
            httpRoutes.Route("/AktivovatNaradi").Using(httpStandard)
                .Parametrized(seznamNaradiRest.AktivovatNaradi)
                .To(seznamNaradiRest.AktivovatNaradi)
                .With(new HttpInputJson<AktivovatNaradiCommand>());
            httpRoutes.Route("/DeaktivovatNaradi").Using(httpStandard)
                .Parametrized(seznamNaradiRest.DeaktivovatNaradi)
                .To(seznamNaradiRest.DeaktivovatNaradi)
                .With(new HttpInputJson<AktivovatNaradiCommand>());
            httpRoutes.Route("/DefinovatNaradi").Using(httpStandard)
                .Parametrized(seznamNaradiRest.DefinovatNaradi)
                .To(seznamNaradiRest.DefinovatNaradi)
                .With(new HttpInputJson<AktivovatNaradiCommand>());

            _bus.Publish(new SystemEvents.SystemInit());
        }

        public void Stop()
        {
            _bus.Publish(new SystemEvents.SystemShutdown());
        }
    }
}
