using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpServerDispatcher : IHttpServerDispatcher
    {
        private IHttpRouter _router;
        public HttpServerDispatcher(IHttpRouter router)
        {
            _router = router;
        }

        public Task DispatchRequest(IHttpServerRawContext context)
        {
            try
            {
                var route = _router.FindRoute(context.Url);
                if (route == null)
                {
                    context.StatusCode = 404;
                    return TaskUtils.CompletedTask();
                }
                else
                    return route.Handler.Handle(context);
            }
            catch
            {
                context.StatusCode = 500;
                return TaskUtils.CompletedTask();
            }
        }
    }
}
