using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpOutputDirect : IHttpOutputProcessor
    {
        private HashSet<Type> _handledTypes;

        public HttpOutputDirect()
        {
            _handledTypes = new HashSet<Type>(new[] { 
                typeof(HttpServerResponse), 
                typeof(HttpServerResponseBuilder),
                typeof(System.IO.Stream),
                typeof(string),
                typeof(StringBuilder),
                typeof(byte[])
            });
        }

        public bool HandlesOutput(HttpServerRequest request, object response)
        {
            return response != null && _handledTypes.Contains(response.GetType());
        }

        public Task<HttpServerResponse> ProcessOutput(HttpServerRequest request, object response)
        {
            if (response is HttpServerResponse)
                return Task.FromResult(response as HttpServerResponse);
            if (response is HttpServerResponseBuilder)
                return Task.FromResult((response as HttpServerResponseBuilder).Build());
            if (response is Stream)
                return Task.FromResult(new HttpServerResponseBuilder()
                    .WithHeader("Content-Type", "application/octet-stream")
                    .WithRawBody(response as Stream)
                    .Build());
            if (response is string)
                return Task.FromResult(new HttpServerResponseBuilder()
                    .WithHeader("Content-Type", "text/plain")
                    .WithRawBody(Encoding.UTF8.GetBytes(response as string))
                    .Build());
            if (response is StringBuilder)
                return Task.FromResult(new HttpServerResponseBuilder()
                    .WithHeader("Content-Type", "text/plain")
                    .WithRawBody(Encoding.UTF8.GetBytes((response as StringBuilder).ToString()))
                    .Build());
            if (response is byte[])
                return Task.FromResult(new HttpServerResponseBuilder()
                    .WithHeader("Content-Type", "application/octet-stream")
                    .WithRawBody(response as byte[])
                    .Build());
            return Task.FromResult(new HttpServerResponseBuilder().WithStatusCode(500).Build());
        }
    }

    public class HttpOutputJson<T> : IHttpOutputProcessor
    {
        public HttpOutputJson(string mode = null)
        {
            switch (mode)
            {
                case "STRICT":
                    _allowEmptyContentType = false;
                    _ignoreContentType = false;
                    break;
                case "IGNORE":
                    _allowEmptyContentType = true;
                    _ignoreContentType = true;
                    break;
                case "DEFAULT":
                default:
                    _allowEmptyContentType = true;
                    _ignoreContentType = false;
                    break;
            }
        }

        private bool _allowEmptyContentType, _ignoreContentType;

        public bool HandlesOutput(HttpServerRequest request, object response)
        {
            return response is T && ContentTypeOk(request);
        }

        private bool ContentTypeOk(HttpServerRequest request)
        {
            if (_ignoreContentType)
                return true;
            var contentType = request.Headers.ContentType;
            if (contentType == "application/json")
                return true;
            if (string.IsNullOrEmpty(contentType) && _allowEmptyContentType)
                return true;
            return false;
        }

        public Task<HttpServerResponse> ProcessOutput(HttpServerRequest request, object response)
        {
            throw new NotImplementedException();
        }
    }
}
