﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
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
        public async Task<HttpClientResponse> Execute(HttpClientRequest request)
        {
            var webRequest = HttpWebRequest.CreateHttp(request.Url);
            webRequest.AllowAutoRedirect = false;
            webRequest.Method = request.Method;
            CopyHeadersToRequest(request, webRequest);
            await WriteRequestBody(request, webRequest);

            var response = new HttpClientResponse();
            using (var webResponse = await GetWebResponse(webRequest))
            {
                response.StatusCode = (int)webResponse.StatusCode;
                CopyHeadersToResponse(response, webResponse);
                await GetReponseBody(response, webResponse);
            }
            return response;
        }

        private static async Task<HttpWebResponse> GetWebResponse(HttpWebRequest webRequest)
        {
            try
            {
                return (HttpWebResponse)await webRequest.GetResponseAsync();
            }
            catch (WebException ex)
            {
                return (HttpWebResponse)ex.Response;
            }
        }

        private static void CopyHeadersToRequest(HttpClientRequest request, HttpWebRequest webRequest)
        {
            foreach (var header in request.Headers)
                webRequest.Headers.Add(header.Name, header.Value);
        }

        private static async Task WriteRequestBody(HttpClientRequest request, HttpWebRequest webRequest)
        {
            if (request.Body != null)
                using (var stream = await webRequest.GetRequestStreamAsync())
                    await stream.WriteAsync(request.Body, 0, request.Body.Length);
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

        private static async Task GetReponseBody(HttpClientResponse response, HttpWebResponse webResponse)
        {
            using (var responseStream = webResponse.GetResponseStream())
            using (var memoryStream = new MemoryStream())
            {
                var buffer = new byte[32 * 1024];
                int read;
                while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    memoryStream.Write(buffer, 0, read);
                response.Body = memoryStream.ToArray();
            }
        }
    }
}