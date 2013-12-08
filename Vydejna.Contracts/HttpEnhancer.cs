using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpUrlEnhancer : IHttpRequestEnhancer
    {
        public Task<HttpServerRequest> Process(HttpServerRequest request, IEnumerable<RequestParameter> routeParameters)
        {
            var parsed = ParametrizedUrl.ParseQueryString(request.Url);
            foreach (var parameter in parsed)
                request.AddParameter(parameter);
            foreach (var parameter in routeParameters)
                request.AddParameter(parameter);
            return TaskResult.GetCompletedTask(request);
        }
    }
}
