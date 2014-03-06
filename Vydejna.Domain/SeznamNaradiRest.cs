using ServiceLib;
using System;
using System.Net;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiRest
    {
        private IQueueExecution _executor;
        private IHandle<CommandExecution<AktivovatNaradiCommand>> _aktivovat;
        private IHandle<CommandExecution<DeaktivovatNaradiCommand>> _deaktivovat;
        private IHandle<CommandExecution<DefinovatNaradiCommand>> _definovat;
        private IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> _seznamNaradi;
        private IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> _kontrolaUnikatnosti;

        public SeznamNaradiRest(
            IQueueExecution executor,
            IHandle<CommandExecution<AktivovatNaradiCommand>> aktivovat,
            IHandle<CommandExecution<DeaktivovatNaradiCommand>> deaktivovat,
            IHandle<CommandExecution<DefinovatNaradiCommand>> definovat,
            IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> seznamNaradi,
            IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> kontrolaUnikatnosti
            )
        {
            _executor = executor;
            _aktivovat = aktivovat;
            _deaktivovat = deaktivovat;
            _definovat = definovat;
            _seznamNaradi = seznamNaradi;
            _kontrolaUnikatnosti = kontrolaUnikatnosti;
        }

        public void RegisterHttpHandlers(IHttpRouteCommonConfigurator config)
        {
            config.Route("AktivovatNaradi").To(AktivovatNaradi);
            config.Route("DeaktivovatNaradi").To(DeaktivovatNaradi);
            config.Route("DefinovatNaradi").To(DefinovatNaradi);
            config.Route("SeznamNaradi").To(NacistSeznamNaradi);
            config.Route("OveritUnikatnost").To(OveritUnikatnost);
        }

        public void AktivovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<AktivovatNaradiCommand>(context, _aktivovat);
        }
        public void DeaktivovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<DeaktivovatNaradiCommand>(context, _deaktivovat);
        }
        public void DefinovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<DefinovatNaradiCommand>(context, _definovat);
        }

        private void HandleCommand<TCommand>(IHttpServerStagedContext context, IHandle<CommandExecution<TCommand>> handler)
        {
            try
            {
                var command = context.InputSerializer.Deserialize<TCommand>(context.InputString);
                var execution = new CommandExecution<TCommand>(command, () => OnCommandCompleted(context, null), ex => OnCommandCompleted(context, ex));
                _executor.Enqueue(handler, execution);
            }
            catch (Exception ex)
            {
                OnCommandCompleted(context, ex);
            }
        }

        public void OnCommandCompleted(IHttpServerStagedContext context, Exception exception)
        {
            if (exception == null)
                context.StatusCode = (int)HttpStatusCode.NoContent;
            else
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                context.OutputHeaders.ContentType = "text/plain";
                context.OutputString = exception.ToString();
            }
            context.Close();
        }

        public void NacistSeznamNaradi(IHttpServerStagedContext context)
        {
            try
            {
                int stranka = context.Parameter("stranka").AsInteger().Default(0).Get();
                var execution = new QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(
                    new ZiskatSeznamNaradiRequest(stranka),
                    result => OnQueryCompleted(context, result),
                    exception => OnQueryFailed(context, exception));
                _executor.Enqueue(_seznamNaradi, execution);
            }
            catch (Exception exception)
            {
                OnQueryFailed(context, exception);
            }
        }

        public void OveritUnikatnost(IHttpServerStagedContext context)
        {
            try
            {
                string vykres = context.Parameter("vykres").AsString().Mandatory().Get();
                string rozmer = context.Parameter("rozmer").AsString().Mandatory().Get();
                var execution = new QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(
                    new OvereniUnikatnostiRequest(vykres, rozmer),
                    result => OnQueryCompleted(context, result),
                    exception => OnQueryFailed(context, exception));
                _executor.Enqueue(_kontrolaUnikatnosti, execution);
            }
            catch (Exception exception)
            {
                OnQueryFailed(context, exception);
            }
        }

        private void OnQueryCompleted<T>(IHttpServerStagedContext context, T result)
        {
            try
            {
                context.StatusCode = (int)HttpStatusCode.OK;
                context.OutputHeaders.ContentType = context.OutputSerializer.ContentType;
                context.OutputString = context.OutputSerializer.Serialize(result);
            }
            finally
            {
                context.Close();
            }
        }

        private void OnQueryFailed(IHttpServerStagedContext context, Exception exception)
        {
            try
            {
                context.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.OutputHeaders.ContentType = "text/plain";
                context.OutputString = exception.ToString();
            }
            finally
            {
                context.Close();
            }
        }

    }
}
