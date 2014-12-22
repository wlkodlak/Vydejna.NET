using System;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpServerDispatcher : IHttpServerDispatcher
    {
        private static readonly HttpServerTraceSource Logger = new HttpServerTraceSource("ServiceLib.HttpServer");
        private readonly IHttpRouter _router;

        public HttpServerDispatcher(IHttpRouter router)
        {
            _router = router;
        }

        public async Task DispatchRequest(IHttpServerRawContext context)
        {
            try
            {
                var route = _router.FindRoute(context.Url);
                if (route == null)
                {
                    Logger.NoRouteFound(context.Url);
                    context.StatusCode = 404;
                }
                else
                {
                    await route.Handler.Handle(context, route.RouteParameters);
                }
            }
            catch (Exception exception)
            {
                context.StatusCode = 500;
                Logger.DispatchFailed(context.Url, exception);
            }
        }
    }
}