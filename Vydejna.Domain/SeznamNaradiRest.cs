using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using System.Net;

namespace Vydejna.Domain
{
    public class SeznamNaradiRest
    {
        private IWriteSeznamNaradi _writeSvc;
        private IReadSeznamNaradi _readSvc;
        private IQueueExecution _executor;

        public SeznamNaradiRest(IWriteSeznamNaradi writeSvc, IReadSeznamNaradi readSvc, IQueueExecution executor)
        {
            _writeSvc = writeSvc;
            _readSvc = readSvc;
            _executor = executor;
        }

        public void RegisterHttpHandlers(IHttpRouteCommonConfigurator config)
        {
            config.Route("AktivovatNaradi").To(AktivovatNaradi);
            config.Route("DeaktivovatNaradi").To(DeaktivovatNaradi);
            config.Route("DefinovatNaradi").To(DefinovatNaradi);
        }

        public void AktivovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<AktivovatNaradiCommand>(context, _writeSvc);
        }
        public void DeaktivovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<DeaktivovatNaradiCommand>(context, _writeSvc);
        }
        public void DefinovatNaradi(IHttpServerStagedContext context)
        {
            HandleCommand<DefinovatNaradiCommand>(context, _writeSvc);
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
                int offset = context.Parameter("offset").AsInteger().Default(0).Get();
                int pocet = context.Parameter("pocet").AsInteger().Default(int.MaxValue).Get();
                var execution = new QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(
                    new ZiskatSeznamNaradiRequest(offset, pocet), 
                    result => OnQueryCompleted(context, result), 
                    exception => OnQueryFailed(context, exception));
                _executor.Enqueue(_readSvc, execution);
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
                _executor.Enqueue(_readSvc, execution);
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
