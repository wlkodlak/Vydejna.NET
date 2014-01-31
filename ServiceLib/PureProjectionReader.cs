using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace ServiceLib
{
    public interface IPureProjectionReader<TState> : IDisposable
    {
        void Get(string partition, Action<TState> onLoaded, Action<Exception> onError);
    }
    public class PureProjectionReader<TState> : IPureProjectionReader<TState>
    {
        private class GetStateFinished : IQueuedExecutionDispatcher
        {
            private Action<TState> _onLoaded;
            private TState _state;
            public GetStateFinished(Action<TState> onLoaded, TState state)
            {
                _onLoaded = onLoaded;
                _state = state;
            }
            public void Execute()
            {
                _onLoaded(_state);
            }
        }

        private readonly MemoryCache<TState> _cache;
        private readonly IDocumentFolder _store;
        private readonly IPureProjectionSerializer<TState> _serializer;
        private INotifyChange _notification;
        private int _isListening;
        private IDisposable _listener;
        public int _validity;
        public int _expiration;

        public PureProjectionReader(IDocumentFolder store, IPureProjectionSerializer<TState> serializer, INotifyChange notification, IQueueExecution executor, ITime time)
        {
            _store = store;
            _serializer = serializer;
            _notification = notification;
            _cache = new MemoryCache<TState>(executor, time);
            _validity = 0;
            _expiration = 60000;
        }

        public void Get(string partition, Action<TState> onLoaded, Action<Exception> onError)
        {
            if (Interlocked.CompareExchange(ref _isListening, 1, 0) == 0)
                _listener = _notification.Register(OnNotify);
            _cache.Get(partition, (v, s) => onLoaded(s), onError, LoadPartition);
        }

        private class LoadPartitionWorker
        {
            private PureProjectionReader<TState> _parent;
            private IMemoryCacheLoad<TState> _load;

            public LoadPartitionWorker(PureProjectionReader<TState> parent, IMemoryCacheLoad<TState> load)
            {
                _parent = parent;
                _load = load;
            }

            public void Execute()
            {
                if (_load.OldValueAvailable)
                    _parent._store.GetNewerDocument(_load.Key, _load.OldVersion, OnLoaded, OnNotChanged, OnMissing, OnRefreshError);
                else
                    _parent._store.GetDocument(_load.Key, OnLoaded, OnMissing, OnLoadError);
            }

            private void OnLoaded(int version, string contents)
            {
                var value = _parent._serializer.Deserialize(contents);
                _load.Expires(_parent._validity, _parent._expiration).SetLoadedValue(version, value);                
            }

            private void OnNotChanged()
            {
                _load.Expires(_parent._validity, _parent._expiration).ValueIsStillValid();
            }

            private void OnMissing()
            {
                _load.SetLoadedValue(0, _parent._serializer.InitialState());
            }

            private void OnRefreshError(Exception exception)
            {
                _load.Expires(0, _parent._expiration).ValueIsStillValid();
            }

            private void OnLoadError(Exception exception)
            {
                _load.LoadingFailed(exception);
            }
        }

        private void LoadPartition(IMemoryCacheLoad<TState> load)
        {
            new LoadPartitionWorker(this, load).Execute();
        }

        private void OnNotify(string partition, int version)
        {
            _cache.Invalidate(partition);
        }

        public void Dispose()
        {
            if (_listener != null)
                _listener.Dispose();
        }

        public void SetupExpiration(int validity, int expiration)
        {
            _validity = validity;
            _expiration = expiration; 
        }
    }
}
