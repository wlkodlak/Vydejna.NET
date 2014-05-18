using ServiceLib;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        private Task ProvestPrikaz<T>(IHttpServerStagedContext ctx)
            where T : class
        {
            return TaskUtils.FromEnumerable(ProvestPrikazInternal<T>(ctx)).GetTask();
        }

        private IEnumerable<Task> ProvestPrikazInternal<T>(IHttpServerStagedContext ctx)
            where T : class
        {
            T cmd = null;

            try
            {
                cmd = ctx.InputSerializer.Deserialize<T>(ctx.InputString);
            }
            catch (Exception exception)
            {
                ctx.StatusCode = 400;
                ctx.OutputHeaders.ContentType = "text/plain";
                ctx.OutputString = exception.InnerException.Message;
            }
            if (cmd != null)
            {
                var task = _publisher.SendCommand(cmd);
                yield return task;

                if (task.Exception == null)
                {
                    ctx.StatusCode = 204;
                }
                else
                {
                    ctx.StatusCode = 400;
                    ctx.OutputHeaders.ContentType = "text/plain";
                    ctx.OutputString = task.Exception.InnerException.Message;
                }
            }

            yield return TaskUtils.CompletedTask();
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
