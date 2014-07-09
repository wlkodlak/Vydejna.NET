using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class DomainRestInterface
    {
        private IPublisher _publisher;
        private string _trackingUrlPrefix;
        
        public DomainRestInterface(IPublisher publisher, string trackingUrlPrefix)
        {
            _publisher = publisher;
            _trackingUrlPrefix = trackingUrlPrefix;
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
            CommandResult result = null;

            try
            {
                if (ctx.InputSerializer != null && !string.IsNullOrEmpty(ctx.InputString))
                    cmd = ctx.InputSerializer.Deserialize<T>(ctx.InputString);
            }
            catch (Exception exception)
            {
                var message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                result = CommandResult.From(new CommandError("POSTDATA", "PARSE", message));
            }
            if (cmd != null)
            {
                var task = _publisher.SendCommand(cmd);
                yield return task;

                if (task.Exception != null)
                {
                    result = new CommandResult(
                        CommandResultStatus.InternalError,
                        task.Exception.InnerExceptions.Select(ex => new CommandError("", ex.GetType().Name, ex.Message)).ToList(),
                        null);
                }
                else if (!task.IsCanceled)
                {
                    result = task.Result;
                }
            }
            else if (result == null)
            {
                result = CommandResult.From(new CommandError("POSTDATA", "REQUIRED", "Command data required"));
            }
            if (result == null)
            {
                ctx.StatusCode = 500;
            }
            else if (result.Status == CommandResultStatus.Success)
            {
                ctx.StatusCode = 204;
                ctx.OutputHeaders.Add("X-Completion-Tracking", string.Concat(_trackingUrlPrefix, result.TrackingId));
            }
            else
            {
                ctx.StatusCode = result.Status == CommandResultStatus.InternalError ? 500 : 400;
                if (ctx.OutputSerializer != null)
                {
                    ctx.OutputHeaders.ContentType = ctx.OutputSerializer.ContentType;
                    ctx.OutputString = ctx.OutputSerializer.Serialize(result);
                }
            }
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
