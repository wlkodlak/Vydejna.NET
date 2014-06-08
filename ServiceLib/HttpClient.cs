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
        public Task<HttpClientResponse> Execute(HttpClientRequest request)
        {
            return TaskUtils.FromEnumerable<HttpClientResponse>(ExecuteInternal(request)).GetTask();
        }

        private IEnumerable<Task> ExecuteInternal(HttpClientRequest request)
        {
            var webRequest = (HttpWebRequest)HttpWebRequest.Create(request.Url);
            webRequest.AllowAutoRedirect = false;
            webRequest.Method = request.Method;
            CopyHeadersToRequest(request, webRequest);

            var response = new HttpClientResponse();

            if (request.Body != null && request.Body.Length > 0)
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
            yield return TaskUtils.FromResult(response);
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
