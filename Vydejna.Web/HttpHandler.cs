using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Threading;
using System.Threading.Tasks;

namespace Vydejna.Web
{
    public class HttpHandler : IHttpHandler
    {
        private IHttpServerDispatcher _dispatcher;

        public HttpHandler(IHttpServerDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            EndProcessRequest(BeginProcessRequest(context, null, null));
        }

        public IAsyncResult BeginProcessRequest(HttpContext nativeContext, AsyncCallback callback, object state)
        {
            var tcs = new TaskCompletionSource<object>(state);
            var rawContext = new AspNetContext(nativeContext);
            _dispatcher.DispatchRequest(rawContext).ContinueWith(task =>
            {
                if (task.IsFaulted)
                    tcs.SetException(task.Exception.InnerExceptions);
                else if (task.IsCanceled)
                    tcs.SetCanceled();
                else
                    tcs.SetResult(null);

                if (callback != null)
                    callback(tcs.Task);
            });
            return tcs.Task;
        }

        public void EndProcessRequest(IAsyncResult result)
        {
            var task = (Task)result;
            try
            {
                task.Wait();
            }
            catch (AggregateException exception)
            {
                throw exception.InnerException.PreserveStackTrace();
            }
        }

        private class AspNetContext : IHttpServerRawContext
        {
            private List<RequestParameter> _routeParameters;
            private RequestHeaders _requestHeaders;
            private ResponseHeaders _responseHeaders;
            private string _url, _method, _clientAddress;
            private System.IO.Stream _inputStream;
            private HttpResponse _response;

            public AspNetContext(HttpContext nativeContext)
            {
                _method = nativeContext.Request.HttpMethod;
                _url = nativeContext.Request.Url.OriginalString;
                _clientAddress = nativeContext.Request.UserHostAddress;
                _inputStream = nativeContext.Request.InputStream;
                _response = nativeContext.Response;
                _routeParameters = new List<RequestParameter>();
                _requestHeaders = new RequestHeaders(nativeContext.Request);
                _responseHeaders = new ResponseHeaders(nativeContext.Response);
            }

            public string Method
            {
                get { return _method; }
            }

            public string Url
            {
                get { return _url; }
            }

            public string ClientAddress
            {
                get { return _clientAddress; }
            }

            public int StatusCode
            {
                get { return _response.StatusCode; }
                set { _response.StatusCode = value; }
            }

            public System.IO.Stream InputStream
            {
                get { return _inputStream; }
            }

            public System.IO.Stream OutputStream
            {
                get { return _response.OutputStream; }
            }

            public IList<RequestParameter> RouteParameters
            {
                get { return _routeParameters; }
            }

            public IHttpServerRawHeaders InputHeaders
            {
                get { return _requestHeaders; }
            }

            public IHttpServerRawHeaders OutputHeaders
            {
                get { return _responseHeaders; }
            }
        }

        private class RequestHeaders : IHttpServerRawHeaders
        {
            private List<KeyValuePair<string, string>> _data;
            
            public RequestHeaders(HttpRequest request)
            {
                _data = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < request.Headers.Count; i++)
                {
                    var name = request.Headers.GetKey(i);
                    foreach (string value in request.Headers.GetValues(i))
                        _data.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            public void Add(string name, string value)
            {
                throw new InvalidOperationException("Request header collection is read only");
            }

            public void Clear()
            {
                throw new InvalidOperationException("Request header collection is read only");
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                return _data.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private class ResponseHeaders : IHttpServerRawHeaders
        {
            private HttpResponse _response;
            
            public ResponseHeaders(HttpResponse response)
            {
                _response = response;
            }

            public void Add(string name, string value)
            {
                _response.Headers.Add(name, value);
            }

            public void Clear()
            {
                _response.ClearHeaders();
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                var list = new List<KeyValuePair<string, string>>(_response.Headers.Count);
                for (int i = 0; i < _response.Headers.Count; i++)
                {
                    var name = _response.Headers.GetKey(i);
                    foreach (string value in _response.Headers.GetValues(i))
                        list.Add(new KeyValuePair<string, string>(name, value));
                }
                return list.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
