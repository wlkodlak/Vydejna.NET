using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;

namespace Vydejna.Contracts
{
    public interface IHttpServerDispatcher
    {
        Task<HttpServerResponse> ProcessRequest(HttpServerRequest request);
    }

    public class HttpServer : IDisposable
    {
        private HttpListener _listener;
        private CancellationTokenSource _cancel;
        private List<Task> _workers;
        private IHttpServerDispatcher _dispatcher;
        private List<string> _prefixes;

        public HttpServer(IEnumerable<string> prefixes, IHttpServerDispatcher dispatcher)
        {
            _listener = new HttpListener();
            _prefixes = prefixes.ToList();
            _dispatcher = dispatcher;
        }
        public void Start()
        {
            _prefixes.ForEach(_listener.Prefixes.Add);
            _listener.Start();
            _cancel = new CancellationTokenSource();
            _workers =
                Enumerable.Range(0, Environment.ProcessorCount * 4).Select(i =>
                    Task.Factory.StartNew(WorkerFunc, _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current))
                .ToList();
        }

        public void Stop()
        {
            _listener.Stop();
            _cancel.Cancel();
            _cancel.Dispose();
            _workers.ForEach(t => t.Wait());
        }

        void IDisposable.Dispose()
        {
            Stop();
            _listener.Close();
        }

        private void WorkerFunc()
        {
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var task = ProcessRequest(_listener.GetContext());
                    task.ContinueWith(EatExceptions, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }
            }
            catch (HttpListenerException)
            {
            }
        }

        private void EatExceptions(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var request = await Task.Factory.StartNew(() => CreateRequest(context.Request));
            var response = await _dispatcher.ProcessRequest(request);
            await WriteResponse(context.Response, response);
        }

        private HttpServerRequest CreateRequest(HttpListenerRequest listenerRequest)
        {
            var httpRequest = new HttpServerRequest();
            httpRequest.Method = listenerRequest.HttpMethod;
            httpRequest.Url = listenerRequest.RawUrl;
            for (int i = 0; i < listenerRequest.Headers.Count; i++)
            {
                var name = listenerRequest.Headers.GetKey(i);
                foreach (string value in listenerRequest.Headers.GetValues(i))
                    httpRequest.Headers.Add(name, value);
            }
            httpRequest.PostDataStream = listenerRequest.InputStream;
            return httpRequest;
        }

        private async Task WriteResponse(HttpListenerResponse listenerResponse, HttpServerResponse httpResponse)
        {
            listenerResponse.StatusCode = httpResponse.StatusCode;
            listenerResponse.Headers.Clear();
            foreach (var header in httpResponse.Headers)
                listenerResponse.Headers.Add(header.Name, header.Value);
            using (var stream = listenerResponse.OutputStream)
            {
                if (httpResponse.StreamBody != null)
                {
                    var buffer = new byte[64 * 1024];
                    var inputStream = httpResponse.StreamBody;
                    while (true)
                    {
                        int read = await inputStream.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;
                        await stream.WriteAsync(buffer, 0, read);
                    }
                }
                else if (httpResponse.RawBody != null && httpResponse.RawBody.Length > 0)
                    await stream.WriteAsync(httpResponse.RawBody, 0, httpResponse.RawBody.Length);
            }
        }
    }
}
