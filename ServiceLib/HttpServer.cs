using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace ServiceLib
{
    public interface IHttpServerDispatcher
    {
        Task DispatchRequest(IHttpServerRawContext context);
    }

    public class HttpServer : IProcessWorker
    {
        private HttpListener _listener;
        private List<Task> _workers;
        private IHttpServerDispatcher _dispatcher;
        private List<string> _prefixes;
        private int _workerCount;
        private ProcessState _processState;
        private Action<ProcessState> _onStateChanged;
        private TaskScheduler _scheduler;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.HttpServer");

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
                Logger.DebugFormat("Starting with prefixes: {0}", string.Join(", ", _prefixes));
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
            Logger.Debug("Stopping");
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
                    TaskUtils.FromEnumerable(ProcessRequestInternal(_listener.GetContext())).CatchAll().UseScheduler(_scheduler).GetTask();
                }
            }
            catch (HttpListenerException)
            {
            }
        }

        private IEnumerable<Task> ProcessRequestInternal(HttpListenerContext context)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var rawContext = new HttpServerListenerContext(context, null);
                var taskDispatch = _dispatcher.DispatchRequest(rawContext);
                yield return taskDispatch;
            }
            finally
            {
                context.Response.Close();
                stopwatch.Stop();
                Logger.InfoFormat("Request {0} {1} processed in {2} ms", 
                    context.Request.HttpMethod, context.Request.Url, stopwatch.ElapsedMilliseconds);
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

    public class HttpServerListenerContext : IHttpServerRawContext
    {
        private HttpListenerRequest _request;
        private HttpListenerResponse _response;
        private Stream _inputStream;
        private string _clientAddress;
        private IList<RequestParameter> _routeParameters;
        private Action _onCompleted;
        private string _url;

        public HttpServerListenerContext(HttpListenerContext listenerContext, Action onCompleted)
        {
            _request = listenerContext.Request;
            _url = _request.Url.OriginalString;
            _response = listenerContext.Response;
            _clientAddress = _request.RemoteEndPoint.Address.ToString();
            _routeParameters = new List<RequestParameter>();
            _inputStream = _request.HasEntityBody ? _request.InputStream : null;
            InputHeaders = new HttpServerListenerRequestHeaders(_request);
            OutputHeaders = new HttpServerListenerResponseHeaders(_response);
            _onCompleted = onCompleted;
        }

        public string Method { get { return _request.HttpMethod; } }
        public string Url { get { return _url; } }
        public string ClientAddress { get { return _clientAddress; } }
        public int StatusCode { get { return _response.StatusCode; } set { _response.StatusCode = value; } }
        public Stream InputStream { get { return _inputStream; } }
        public Stream OutputStream { get { return _response.OutputStream; } }
        public IList<RequestParameter> RouteParameters { get { return _routeParameters; } }
        public IHttpServerRawHeaders InputHeaders { get; private set; }
        public IHttpServerRawHeaders OutputHeaders { get; private set; }
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
