using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IHttpClient
    {
        void Execute(HttpClientRequest request, Action<HttpClientResponse> onCompleted, Action<Exception> onError);
    }

    public class HttpClientRequest
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public List<HttpClientHeader> Headers { get; private set; }
        public byte[] Body { get; set; }
        public HttpClientRequest() { this.Headers = new List<HttpClientHeader>(); }
    }

    public class HttpClientResponse
    {
        public int StatusCode { get; set; }
        public List<HttpClientHeader> Headers { get; private set; }
        public byte[] Body { get; set; }
        public HttpClientResponse() { this.Headers = new List<HttpClientHeader>(); }
    }

    public class HttpClientHeader
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public HttpClientHeader(string name, string value)
        {
            this.Name = name;
            this.Value = value;
        }
    }

    public class HttpClient : IHttpClient
    {
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(HttpClient));

        public void Execute(HttpClientRequest request, Action<HttpClientResponse> onCompleted, Action<Exception> onError)
        {
            new Execution(this, request, onCompleted, onError).Execute();
        }

        private class Execution
        {
            private HttpClient _parent;
            private HttpClientRequest _request;
            private HttpClientResponse _response;
            private Action<HttpClientResponse> _onCompleted;
            private Action<Exception> _onError;
            private Stream _requestStream;
            private Stream _responseStream;
            private HttpWebResponse _webResponse;
            private HttpWebRequest _webRequest;
            private MemoryStream _memoryStream;
            private byte[] _copyBuffer;

            public Execution(HttpClient parent, HttpClientRequest request, Action<HttpClientResponse> onCompleted, Action<Exception> onError)
            {
                this._parent = parent;
                this._request = request;
                this._onCompleted = onCompleted;
                this._onError = onError;
            }

            public void Execute()
            {
                if (_parent._log.IsDebugEnabled)
                    LogRequest(_request);
                _webRequest = HttpWebRequest.CreateHttp(_request.Url);
                _webRequest.AllowAutoRedirect = false;
                _webRequest.Method = _request.Method;
                CopyHeadersToRequest(_request, _webRequest);
                _response = new HttpClientResponse();

                if (_request.Body != null)
                    _webRequest.GetRequestStreamAsync().ContinueWith(ConnectedToRequestStream);
                else
                    _webRequest.GetResponseAsync().ContinueWith(ReceivedWebResponse);
            }

            private void ConnectedToRequestStream(Task<Stream> task)
            {
                if (task.Exception != null)
                    _onError(task.Exception.GetBaseException());
                else
                {
                    _requestStream = task.Result;
                    _requestStream.WriteAsync(_request.Body, 0, _request.Body.Length).ContinueWith(SentRequestData);
                }
            }

            private void SentRequestData(Task task)
            {
                _requestStream.Dispose();
                if (task.Exception != null)
                    _onError(task.Exception.GetBaseException());
                else
                    _webRequest.GetResponseAsync().ContinueWith(ReceivedWebResponse);
            }

            private void ReceivedWebResponse(Task<WebResponse> task)
            {
                var exception = task.Exception == null ? null : task.Exception.GetBaseException();
                var webException = exception as WebException;
                if (webException != null)
                {
                    _webResponse = (HttpWebResponse)webException.Response;
                    ProcessWebResponse();
                }
                else if (exception != null)
                    _onError(exception);
                else
                {
                    _webResponse = (HttpWebResponse)task.Result;
                    ProcessWebResponse();
                }
            }

            private void ProcessWebResponse()
            {
                _response.StatusCode = (int)_webResponse.StatusCode;
                CopyHeadersToResponse(_response, _webResponse);
                _responseStream = _webResponse.GetResponseStream();
                _memoryStream = new MemoryStream();
                _copyBuffer = new byte[32 * 1024];
                _responseStream.ReadAsync(_copyBuffer, 0, _copyBuffer.Length).ContinueWith(CopiedResponseStream);
            }

            private void CopiedResponseStream(Task<int> task)
            {
                if (task.Exception != null)
                {
                    _responseStream.Dispose();
                    _onError(task.Exception.GetBaseException());
                }
                else
                {
                    int read = task.Result;
                    if (read == 0)
                    {
                        _responseStream.Dispose();
                        _response.Body = _memoryStream.ToArray();
                        FinishedReadingResponse();
                    }
                    else
                    {
                        _memoryStream.Write(_copyBuffer, 0, read);
                        _responseStream.ReadAsync(_copyBuffer, 0, _copyBuffer.Length).ContinueWith(CopiedResponseStream);
                    }
                }
            }

            private void FinishedReadingResponse()
            {
                if (_parent._log.IsDebugEnabled)
                    LogResponse(_response);
                _onCompleted(_response);
            }

            private void LogRequest(HttpClientRequest request)
            {
                var sb = new StringBuilder();
                sb.AppendFormat("Execute - request {0} {1}\r\n", request.Method, request.Url);
                foreach (var header in request.Headers)
                    sb.AppendFormat("{0}: {1}\r\n", header.Name, header.Value);
                sb.AppendLine();
                if (request.Body != null)
                    sb.Append(Encoding.UTF8.GetString(request.Body));
                _parent._log.Debug(sb.ToString());
            }

            private void LogResponse(HttpClientResponse response)
            {
                var sb = new StringBuilder();
                sb.AppendFormat("Execute - response {0}\r\n", response.StatusCode);
                foreach (var header in response.Headers)
                    sb.AppendFormat("{0}: {1}\r\n", header.Name, header.Value);
                sb.AppendLine();
                if (response.Body != null)
                    sb.Append(Encoding.UTF8.GetString(response.Body));
                _parent._log.Debug(sb.ToString());
            }

            private static DateTime ParseDateHeader(string headerValue)
            {
                return DateTime.ParseExact(headerValue,
                        "ddd, dd MMM yyyy HH:mm:ss 'UTC'",
                        CultureInfo.InvariantCulture.DateTimeFormat,
                        DateTimeStyles.AssumeUniversal);
            }

            private static void CopyHeadersToRequest(HttpClientRequest request, HttpWebRequest webRequest)
            {
                foreach (var header in request.Headers)
                {
                    switch (header.Name)
                    {
                        case "Accept":
                            webRequest.Accept = header.Value;
                            break;
                        case "Connection":
                            webRequest.Connection = header.Value;
                            break;
                        case "Content-Length":
                            webRequest.ContentLength = int.Parse(header.Value);
                            break;
                        case "Content-Type":
                            webRequest.ContentType = header.Value;
                            break;
                        case "Date":
                            webRequest.Date = ParseDateHeader(header.Value);
                            break;
                        case "Expect":
                            webRequest.Expect = header.Value;
                            break;
                        case "Host":
                            webRequest.Host = header.Value;
                            break;
                        case "If-Modified-Since":
                            webRequest.IfModifiedSince = ParseDateHeader(header.Value);
                            break;
                        case "Referer":
                            webRequest.Referer = header.Value;
                            break;
                        case "Transfer-Encoding":
                            webRequest.TransferEncoding = header.Value;
                            break;
                        case "User-Agent":
                            webRequest.UserAgent = header.Value;
                            break;
                        default:
                            webRequest.Headers.Add(header.Name, header.Value);
                            break;
                    }
                }
            }

            private static void CopyHeadersToResponse(HttpClientResponse response, HttpWebResponse webResponse)
            {
                for (int i = 0; i < webResponse.Headers.Count; i++)
                {
                    var name = webResponse.Headers.GetKey(i);
                    foreach (var value in webResponse.Headers.GetValues(i))
                        response.Headers.Add(new HttpClientHeader(name, value));
                }
            }
        }
    }
}
