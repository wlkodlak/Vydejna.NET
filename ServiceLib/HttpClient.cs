using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
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

        public HttpClientRequest()
        {
            Headers = new List<HttpClientHeader>();
        }
    }

    public class HttpClientResponse
    {
        public int StatusCode { get; set; }
        public List<HttpClientHeader> Headers { get; private set; }
        public byte[] Body { get; set; }

        public HttpClientResponse()
        {
            Headers = new List<HttpClientHeader>();
        }
    }

    public class HttpClientHeader
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public HttpClientHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public class HttpClient : IHttpClient
    {
        private static readonly HttpClientTraceSource Logger = new HttpClientTraceSource("ServiceLib.HttpClient");

        public async Task<HttpClientResponse> Execute(HttpClientRequest request)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var webRequest = (HttpWebRequest)WebRequest.Create(request.Url);
                webRequest.AllowAutoRedirect = false;
                webRequest.Method = request.Method;
                CopyHeadersToRequest(request, webRequest);

                if (request.Body != null && request.Body.Length > 0)
                {
                    using (var requestStream = await webRequest.GetRequestStreamAsync())
                    {
                        await requestStream.WriteAsync(request.Body, 0, request.Body.Length);
                    }
                }

                var response = new HttpClientResponse();

                HttpWebResponse webResponse;
                try
                {
                    webResponse = (HttpWebResponse)await webRequest.GetResponseAsync();
                }
                catch (WebException exception)
                {
                    webResponse = (HttpWebResponse)exception.Response;
                    if (webResponse == null)
                    {
                        Logger.NoResponse(request);
                        return response;
                    }
                }

                response.StatusCode = (int)webResponse.StatusCode;
                CopyHeadersToResponse(response, webResponse);
                var memoryStream = new MemoryStream();
                var copyBuffer = new byte[32 * 1024];
                using (var responseStream = webResponse.GetResponseStream())
                {
                    if (responseStream != null)
                    {
                        while (true)
                        {
                            var readBytes = await responseStream.ReadAsync(copyBuffer, 0, copyBuffer.Length);
                            if (readBytes == 0)
                                break;
                            memoryStream.Write(copyBuffer, 0, readBytes);
                        }
                    }
                }
                response.Body = memoryStream.ToArray();
                Logger.RequestComplete(request, response, stopwatch.ElapsedMilliseconds);
                return response;
            }
            finally
            {
                stopwatch.Stop();
            }
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
            for (var i = 0; i < webResponse.Headers.Count; i++)
            {
                var name = webResponse.Headers.GetKey(i);
                var values = webResponse.Headers.GetValues(i);
                if (values == null)
                    continue;
                foreach (var value in values)
                    response.Headers.Add(new HttpClientHeader(name, value));
            }
        }
    }

    public class HttpClientTraceSource : TraceSource
    {
        public HttpClientTraceSource(string name)
            : base(name)
        {
        }

        public void NoResponse(HttpClientRequest request)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "No response to {Url}");
            msg.SetProperty("Method", false, request.Method);
            msg.SetProperty("Url", false, request.Url);
            msg.Log(this);
        }

        public void RequestComplete(HttpClientRequest request, HttpClientResponse response, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "Request to {Url} finished");
            msg.SetProperty("Method", false, request.Method);
            msg.SetProperty("Url", false, request.Url);
            msg.SetProperty("Status", false, response.StatusCode);
            msg.SetProperty("RequestBody", true, request.Body);
            msg.SetProperty("ResponseBody", true, response.Body);
            msg.Log(this);
        }
    }
}
