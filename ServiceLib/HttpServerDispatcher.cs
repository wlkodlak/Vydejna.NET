using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpServerDispatcher : IHttpServerDispatcher
    {
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(HttpServerDispatcher));
        private IHttpRouter _router;
        public HttpServerDispatcher(IHttpRouter router)
        {
            _router = router;
        }

        public void DispatchRequest(IHttpServerRawContext context)
        {
            try
            {
                var route = _router.FindRoute(context.Url);
                if (route == null)
                {
                    _log.DebugFormat("Raw HTTP request {0}: 404 Not found", context.Url);
                    context.StatusCode = 404;
                    context.Close();
                }
                route.Handler.Handle(context);
            }
            catch (Exception ex)
            {
                _log.WarnFormat("Request {0} threw exception: {1}", context.Url, ex.ToString());
                context.StatusCode = 500;
                context.Close();
            }
        }
    }
}
