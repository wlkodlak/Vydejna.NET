using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace Vydejna.Contracts
{
    public interface IHttpServerDispatcher
    {
        void DispatchRequest(IHttpServerRawContext context);
    }

    public class HttpServer : IDisposable
    {
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(HttpServer));
        private HttpListener _listener;
        private CancellationTokenSource _cancel;
        private List<Task> _workers;
        private IHttpServerDispatcher _dispatcher;
        private List<string> _prefixes;
        private bool _isRunning;
        private int _workerCount;

        public HttpServer(IEnumerable<string> prefixes, IHttpServerDispatcher dispatcher)
        {
            _listener = new HttpListener();
            _prefixes = prefixes.ToList();
            _dispatcher = dispatcher;
            _workerCount = Environment.ProcessorCount * 4;
        }
        public HttpServer SetupWorkerCount(int totalCount)
        {
            _workerCount = totalCount;
            return this;
        }
        public void Start()
        {
            try
            {
                _prefixes.ForEach(_listener.Prefixes.Add);
                _listener.Start();
                _cancel = new CancellationTokenSource();
                _isRunning = true;
                _workers =
                    Enumerable.Range(0, _workerCount).Select(i =>
                        Task.Factory.StartNew(WorkerFunc, _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current))
                    .ToList();
                _log.Debug("HttpServer started");
            }
            catch (Exception ex)
            {
                _log.Error("Could not start HttpServer", ex);
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;
            _listener.Stop();
            _cancel.Cancel();
            _cancel.Dispose();
            Task.WaitAll(_workers.ToArray(), 1000);
            _isRunning = false;
            _log.Debug("HttpServer stopped");
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
                    new RequestHandler(this, _listener.GetContext()).Run();
                }
            }
            catch (HttpListenerException)
            {
            }
        }

        private class RequestHandler
        {
            private HttpServer _parent;
            private HttpListenerContext _context;
            private HttpServerListenerContext _rawContext;
            private Stopwatch _stopwatch;

            public RequestHandler(HttpServer parent, HttpListenerContext context)
            {
                _parent = parent;
                _context = context;
            }

            public void Run()
            {
                Task.Factory.StartNew(Phase1);
            }

            private void Phase1()
            {
                _stopwatch = new Stopwatch();
                _stopwatch.Start();
                _rawContext = new HttpServerListenerContext(_context, OnCompleted);
                _parent._dispatcher.DispatchRequest(_rawContext);
            }
            private void OnCompleted()
            {
                _stopwatch.Stop();
                _parent._log.DebugFormat("Request took {0} ms", _stopwatch.ElapsedMilliseconds);
            }
        }
    }

    public class HttpServerListenerContext : IHttpServerRawContext
    {
        private HttpListenerRequest _request;
        private HttpListenerResponse _response;
        private Stream _inputStream;
        private string _clientAddress;
        private IList<RequestParameter> _routeParameters;
        private Action _onCompleted;

        public HttpServerListenerContext(HttpListenerContext listenerContext, Action onCompleted)
        {
            _request = listenerContext.Request;
            _response = listenerContext.Response;
            _clientAddress = _request.RemoteEndPoint.Address.ToString();
            _routeParameters = new List<RequestParameter>();
            _inputStream = _request.HasEntityBody ? _request.InputStream : null;
            InputHeaders = new HttpServerListenerRequestHeaders(_request);
            OutputHeaders = new HttpServerListenerResponseHeaders(_response);
            _onCompleted = onCompleted;
        }

        public string Method { get { return _request.HttpMethod; } }
        public string Url { get { return _request.RawUrl; } }
        public string ClientAddress { get { return _clientAddress; } }
        public int StatusCode { get { return _response.StatusCode; } set { _response.StatusCode = value; } }
        public Stream InputStream { get { return _inputStream; } }
        public Stream OutputStream { get { return _response.OutputStream; } }
        public IList<RequestParameter> RouteParameters { get { return _routeParameters; } }
        public IHttpServerRawHeaders InputHeaders { get; private set; }
        public IHttpServerRawHeaders OutputHeaders { get; private set; }
        
        public void Close()
        {
            _response.Close(); 
            _onCompleted();
        }
    }

    public class HttpServerListenerRequestHeaders : IHttpServerRawHeaders
    {
        private List<KeyValuePair<string, string>> _data;

        public HttpServerListenerRequestHeaders(HttpListenerRequest request)
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

    public class HttpServerListenerResponseHeaders : IHttpServerRawHeaders
    {
        private HttpListenerResponse _response;

        public HttpServerListenerResponseHeaders(HttpListenerResponse response)
        {
            _response = response;
        }

        public void Add(string name, string value)
        {
            _response.Headers.Add(name, value);
        }

        public void Clear()
        {
            _response.Headers.Clear();
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
