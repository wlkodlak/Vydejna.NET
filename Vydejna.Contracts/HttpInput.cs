using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceStack.Text;

namespace Vydejna.Contracts
{
    public class HttpInputJson<T> : IHttpInputProcessor
    {
        public bool HandlesContentType(string contentType)
        {
            return contentType == "applicaton/json";
        }

        public Task<object> ProcessInput(HttpServerRequest request)
        {
            return Task.Factory.StartNew(() => JsonSerializer.DeserializeFromStream(typeof(T), request.PostDataStream));
        }
    }
}
