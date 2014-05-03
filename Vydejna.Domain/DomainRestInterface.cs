using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class DomainRestInterface
    {
        private IPublisher _publisher;
        
        public DomainRestInterface(IPublisher publisher)
        {
            _publisher = publisher;
        }

        private void ProvestPrikaz<T>(IHttpServerStagedContext ctx)
        {
            try
            {
                var cmd = ctx.InputSerializer.Deserialize<T>(ctx.InputString);
                var exec = new CommandExecution<T>(cmd, () => PrikazUspel(ctx), ex => PrikazSelhal(ctx, ex));
                _publisher.Publish(exec);
            }
            catch (Exception ex)
            {
                PrikazSelhal(ctx, ex);
            }
        }

        private void PrikazUspel(IHttpServerStagedContext ctx)
        {
            ctx.StatusCode = 204;
            ctx.Close();
        }

        private void PrikazSelhal(IHttpServerStagedContext ctx, Exception ex)
        {
            ctx.StatusCode = 400;
            ctx.OutputHeaders.ContentType = "text/plain";
            ctx.OutputString = ex.Message;
            ctx.Close();
        }

        public void Register(IHttpRouteCommonConfigurator config)
        {
            config.Route("seznamnaradi/aktivovat").To(ProvestPrikaz<AktivovatNaradiCommand>);
            config.Route("seznamnaradi/deaktivovat").To(ProvestPrikaz<DeaktivovatNaradiCommand>);
            config.Route("seznamnaradi/definovat").To(ProvestPrikaz<DefinovatNaradiCommand>);
            config.Route("cislovane/prijmoutnavydejnu").To(ProvestPrikaz<CislovaneNaradiPrijmoutNaVydejnuCommand>);
            config.Route("cislovane/vydatdovyroby").To(ProvestPrikaz<CislovaneNaradiVydatDoVyrobyCommand>);
            config.Route("cislovane/prijmoutzvyroby").To(ProvestPrikaz<CislovaneNaradiPrijmoutZVyrobyCommand>);
            config.Route("cislovane/predatkoprave").To(ProvestPrikaz<CislovaneNaradiPredatKOpraveCommand>);
            config.Route("cislovane/prijmoutzopravy").To(ProvestPrikaz<CislovaneNaradiPrijmoutZOpravyCommand>);
            config.Route("cislovane/predatkesesrotovani").To(ProvestPrikaz<CislovaneNaradiPredatKeSesrotovaniCommand>);
            config.Route("necislovane/prijmoutnavydejnu").To(ProvestPrikaz<NecislovaneNaradiPrijmoutNaVydejnuCommand>);
            config.Route("necislovane/vydatdovyroby").To(ProvestPrikaz<NecislovaneNaradiVydatDoVyrobyCommand>);
            config.Route("necislovane/prijmoutzvyroby").To(ProvestPrikaz<NecislovaneNaradiPrijmoutZVyrobyCommand>);
            config.Route("necislovane/predatkoprave").To(ProvestPrikaz<NecislovaneNaradiPredatKOpraveCommand>);
            config.Route("necislovane/prijmoutzopravy").To(ProvestPrikaz<NecislovaneNaradiPrijmoutZOpravyCommand>);
            config.Route("necislovane/predatkesesrotovani").To(ProvestPrikaz<NecislovaneNaradiPredatKeSesrotovaniCommand>);
            config.Route("dodavatele/definovat").To(ProvestPrikaz<DefinovanDodavatelEvent>);
            config.Route("vady/definovat").To(ProvestPrikaz<DefinovanaVadaNaradiEvent>);
            config.Route("pracoviste/definovat").To(ProvestPrikaz<DefinovanoPracovisteEvent>);
            config.Commit();
        }
    }
}
