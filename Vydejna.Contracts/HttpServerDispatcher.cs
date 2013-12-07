﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpServerDispatcher : IHttpServerDispatcher
    {
        private IHttpRouter _router;
        public HttpServerDispatcher(IHttpRouter router)
        {
            _router = router;
        }
        public Task<HttpServerResponse> ProcessRequest(HttpServerRequest request)
        {
            var route = _router.FindRoute(request.Url);
            if (route == null)
                return new HttpServerResponseBuilder().WithStatusCode(404).BuildTask();
            else
                return route.Handler.Handle(request, route.RouteParameters);
        }
    }
}