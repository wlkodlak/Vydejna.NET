using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpServerDispatcher : IHttpServerDispatcher
    {
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(HttpServerDispatcher));
        private IHttpRouter _router;
        public HttpServerDispatcher(IHttpRouter router)
        {
            _router = router;
        }

        public async Task<HttpServerResponse> ProcessRequest(HttpServerRequest request)
        {
            var route = _router.FindRoute(request.Url);
            if (route == null)
            {
                _log.DebugFormat("Raw HTTP request {0}: 404 Not found", request.Url);
                return new HttpServerResponseBuilder().WithStatusCode(404).Build();
            }
            var response = await route.Handler.Handle(request, route.RouteParameters);
            if (response == null)
            {
                _log.WarnFormat("NULL response to {0}", request.Url);
                return new HttpServerResponseBuilder().WithStatusCode(500).Build();
            }
            return response;
        }
    }
}
