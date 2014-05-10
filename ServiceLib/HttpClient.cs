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
        Task<HttpClientResponse> Execute(HttpClientRequest request);
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

        public Task<HttpClientResponse> Execute(HttpClientRequest request)
        {
            return TaskUtils.FromEnumerable<HttpClientResponse>(ExecuteInternal(request)).GetTask();
        }

        private IEnumerable<Task> ExecuteInternal(HttpClientRequest request)
        {
            if (_log.IsDebugEnabled)
                LogRequest(request);
            var webRequest = (HttpWebRequest)HttpWebRequest.Create(request.Url);
            webRequest.AllowAutoRedirect = false;
            webRequest.Method = request.Method;
            CopyHeadersToRequest(request, webRequest);

            var response = new HttpClientResponse();

            if (request.Body != null)
            {
                var taskGetRequestStream = Task.Factory.FromAsync<Stream>(webRequest.BeginGetRequestStream(null, null), webRequest.EndGetRequestStream);
                yield return taskGetRequestStream;
                var requestStream = taskGetRequestStream.Result;

                var taskWrite = Task.Factory.FromAsync(requestStream.BeginWrite(request.Body, 0, request.Body.Length, null, null), requestStream.EndWrite);
                yield return taskWrite;
                taskWrite.Wait();

                requestStream.Dispose();
            }

            var taskGetResponse = Task.Factory.FromAsync<WebResponse>(webRequest.BeginGetResponse(null, null), webRequest.EndGetResponse);
            yield return taskGetResponse;

            HttpWebResponse webResponse;
            if (taskGetResponse.Exception != null)
            {
                var exception = taskGetResponse.Exception.InnerException as WebException;
                if (exception != null)
                    webResponse = (HttpWebResponse)exception.Response;
                else
                    throw taskGetResponse.Exception;
            }
            else
            {
                webResponse = (HttpWebResponse)taskGetResponse.Result;
            }

            response.StatusCode = (int)webResponse.StatusCode;
            CopyHeadersToResponse(response, webResponse);
            var memoryStream = new MemoryStream();
            var copyBuffer = new byte[32 * 1024];
            using (var responseStream = webResponse.GetResponseStream())
            {
                while (true)
                {
                    var taskRead = Task.Factory.FromAsync<int>(responseStream.BeginRead(copyBuffer, 0, copyBuffer.Length, null, null), responseStream.EndRead);
                    yield return taskRead;
                    if (taskRead.Result == 0)
                        break;
                    memoryStream.Write(copyBuffer, 0, taskRead.Result);
                }
            }
            response.Body = memoryStream.ToArray();
            if (_log.IsDebugEnabled)
                LogResponse(response);
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
            _log.Debug(sb.ToString());
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
            _log.Debug(sb.ToString());
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
