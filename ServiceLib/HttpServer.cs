using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IHttpServerDispatcher
    {
        Task DispatchRequest(IHttpServerRawContext context);
    }

    public class HttpServer : IProcessWorker
    {
        private static readonly HttpServerTraceSource Logger = new HttpServerTraceSource("ServiceLib.HttpServer");
        private readonly HttpListener _listener;
        private readonly IHttpServerDispatcher _dispatcher;
        private List<Task> _workers;
        private readonly List<string> _prefixes;
        private int _workerCount;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private TaskScheduler _scheduler;

        public HttpServer(IEnumerable<string> prefixes, IHttpServerDispatcher dispatcher)
        {
            _listener = new HttpListener();
            _prefixes = prefixes.ToList();
            _dispatcher = dispatcher;
            _workerCount = Environment.ProcessorCount * 2;
        }

        public HttpServer SetupWorkerCount(int totalCount)
        {
            _workerCount = totalCount;
            return this;
        }

        public void Start()
        {
            SetProcessState(ProcessState.Starting);
            try
            {
                _prefixes.ForEach(_listener.Prefixes.Add);
                Logger.ServerStarting(_prefixes);
                _listener.Start();
                SetProcessState(ProcessState.Running);
                _workers = Enumerable.Range(0, _workerCount)
                    .Select(i => new Task(WorkerFunc, TaskCreationOptions.LongRunning))
                    .ToList();
                _workers.ForEach(w => w.Start(_scheduler));
            }
            catch
            {
                SetProcessState(ProcessState.Faulted);
            }
        }

        public void Stop(bool immediatelly)
        {
            Logger.ServerStopping();
            if (_processState == ProcessState.Running || _processState == ProcessState.Starting)
            {
                SetProcessState(immediatelly ? ProcessState.Stopping : ProcessState.Pausing);
                _listener.Stop();
                Task.WaitAll(_workers.ToArray(), 1000);
                SetProcessState(ProcessState.Inactive);
            }
        }

        void IDisposable.Dispose()
        {
            Stop(true);
            _listener.Close();
        }

        private void WorkerFunc()
        {
            try
            {
                while (_processState == ProcessState.Running)
                {
                    ProcessRequest(_listener.GetContext());
                }
            }
            catch (HttpListenerException)
            {
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var rawContext = new HttpServerListenerContext(context);
                await _dispatcher.DispatchRequest(rawContext);
                Logger.RequestProcessed(context.Request.HttpMethod, context.Request.Url, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                Logger.RequestFailed(context.Request.HttpMethod, context.Request.Url, exception);
            }
            finally
            {
                context.Response.Close();
                stopwatch.Stop();
            }
        }

        public ProcessState State
        {
            get { return _processState; }
        }

        public void Init(Action<ProcessState> onStateChanged, TaskScheduler scheduler)
        {
            _onStateChanged = onStateChanged;
            _scheduler = scheduler;
            SetProcessState(ProcessState.Inactive);
        }

        public void Pause()
        {
            Stop(false);
        }

        public void Stop()
        {
            Stop(true);
        }

        private void SetProcessState(ProcessState state)
        {
            _processState = state;
            if (_onStateChanged != null)
                _onStateChanged(state);
        }
    }

    public class HttpServerTraceSource : TraceSource
    {
        public HttpServerTraceSource(string name)
            : base(name)
        {
        }

        public void ServerStarting(List<string> prefixes)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 1, "HTTP server starting");
            msg.SetProperty("Prefixed", true, string.Join(", ", prefixes));
            msg.Log(this);
        }

        public void ServerStopping()
        {
            var msg = new LogContextMessage(TraceEventType.Information, 2, "HTTP server stopped");
            msg.Log(this);
        }

        public void RequestProcessed(string httpMethod, Uri url, long elapsedMilliseconds)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 3, "Request to {Url} processed in {Duration} ms");
            msg.SetProperty("Url", false, url);
            msg.SetProperty("Duration", false, elapsedMilliseconds);
            msg.Log(this);
        }

        public void RequestFailed(string httpMethod, Uri url, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 4, "Request to {Url} crashed");
            msg.SetProperty("Url", false, url);
            msg.SetProperty("Exception", false, exception);
            msg.Log(this);
        }

        public void NoRouteFound(string url)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 11, "No route found for {Url}");
            msg.SetProperty("Url", false, url);
            msg.Log(this);
        }

        public void DispatchFailed(string url, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 11, "Dispatch failed for {Url}");
            msg.SetProperty("Url", false, url);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }

    public class HttpServerListenerContext : IHttpServerRawContext
    {
        private readonly HttpListenerRequest _request;
        private readonly HttpListenerResponse _response;
        private readonly Stream _inputStream;
        private readonly string _clientAddress;
        private readonly string _url;

        public HttpServerListenerContext(HttpListenerContext listenerContext)
        {
            _request = listenerContext.Request;
            _url = _request.Url.OriginalString;
            _response = listenerContext.Response;
            _clientAddress = _request.RemoteEndPoint != null ? _request.RemoteEndPoint.Address.ToString() : "";
            _inputStream = _request.HasEntityBody ? _request.InputStream : null;
            InputHeaders = new HttpServerListenerRequestHeaders(_request);
            OutputHeaders = new HttpServerListenerResponseHeaders(_response);
        }

        public string Method
        {
            get { return _request.HttpMethod; }
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

        public Stream InputStream
        {
            get { return _inputStream; }
        }

        public Stream OutputStream
        {
            get { return _response.OutputStream; }
        }

        public IHttpServerRawHeaders InputHeaders { get; private set; }

        public IHttpServerRawHeaders OutputHeaders { get; private set; }
    }

    public class HttpServerListenerRequestHeaders : IHttpServerRawHeaders
    {
        private readonly List<KeyValuePair<string, string>> _data;

        public HttpServerListenerRequestHeaders(HttpListenerRequest request)
        {
            _data = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < request.Headers.Count; i++)
            {
                var name = request.Headers.GetKey(i);
                var values = request.Headers.GetValues(i);
                if (values == null)
                    continue;
                foreach (var value in values)
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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class HttpServerListenerResponseHeaders : IHttpServerRawHeaders
    {
        private readonly HttpListenerResponse _response;

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
            for (var i = 0; i < _response.Headers.Count; i++)
            {
                var name = _response.Headers.GetKey(i);
                var values = _response.Headers.GetValues(i);
                if (values == null) continue;
                foreach (var value in values)
                    list.Add(new KeyValuePair<string, string>(name, value));
            }
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}