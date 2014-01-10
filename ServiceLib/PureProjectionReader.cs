using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace ServiceLib
{
    public interface IPureProjectionReader<TState>
    {
        void Get(string partition, Action<TState> onLoaded, Action<Exception> onError);
    }
    public class PureProjectionReader<TState> : IPureProjectionReader<TState>
    {
        private class Item
        {
            private PureProjectionReader<TState> _parent;
            public string Partition;
            public TState State;
            public bool IsLoading, IsLoaded, IsEvicted, IsWatched;
            public int Version, InvalidatedVersion;
            public IDisposable Watcher;
            public int CacheHits, CacheScore;
            public List<Action<TState>> OnLoadWaiters;
            public List<Action<Exception>> OnErrorWaiters;

            public Item(PureProjectionReader<TState> parent, string partition)
            {
                _parent = parent;
                Partition = partition;
            }

            public void OnDocumentFound(int version, string contents)
            {
                bool watchDocument;
                List<Action<TState>> waiters;
                var state = string.IsNullOrEmpty(contents)
                    ? _parent._serializer.InitialState()
                    : _parent._serializer.Deserialize(contents);
                lock (this)
                {
                    Version = version;
                    State = state;
                    IsLoading = false;
                    IsLoaded = Version != InvalidatedVersion;
                    waiters = OnLoadWaiters;
                    OnLoadWaiters = null;
                    OnErrorWaiters = null;
                    watchDocument = !IsWatched;
                    IsWatched = true;
                }
                if (watchDocument)
                    Watcher = _parent._store.WatchChanges(Partition, version, DocumentChanged);
                foreach (var waiter in waiters)
                    _parent._executor.Enqueue(new GetStateFinished(waiter, state));
            }

            public void DocumentChanged()
            {
                lock (this)
                {
                    InvalidatedVersion = Version;
                    IsLoaded = false;
                }
            }

            public void OnDocumentMissing()
            {
                OnDocumentFound(0, "");
            }

            public void OnError(Exception exception)
            {
                List<Action<Exception>> waiters;
                lock (this)
                {
                    IsLoading = false;
                    IsLoaded = false;
                    waiters = OnErrorWaiters;
                    OnLoadWaiters = null;
                    OnErrorWaiters = null;
                }
                foreach (var waiter in waiters)
                    _parent._executor.Enqueue(waiter, exception);
            }

            public void NotifyUsage()
            {
                if (Interlocked.Increment(ref CacheHits) < _parent._roundLimit)
                    Interlocked.Add(ref CacheScore, _parent._increment);
            }

            public void EndCycle()
            {
                if (IsLoaded)
                {
                    CacheScore = CacheScore >> _parent._roundShift;
                    CacheHits = 0;
                }
            }

            public bool EvictIfNeeded()
            {
                return IsLoaded && CacheScore == 0;
            }

            public void NotifyRemoval()
            {
                IsEvicted = true;
                if (Watcher != null)
                    Watcher.Dispose();
            }
        }

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

        private readonly ConcurrentDictionary<string, Item> _cache;
        private readonly IDocumentFolder _store;
        private readonly IPureProjectionSerializer<TState> _serializer;
        private readonly IQueueExecution _executor;
        private int _increment;
        private int _roundLimit;
        private int _roundShift;

        public PureProjectionReader(IDocumentFolder store, IPureProjectionSerializer<TState> serializer, IQueueExecution executor)
        {
            _store = store;
            _serializer = serializer;
            _executor = executor;
            _cache = new ConcurrentDictionary<string, Item>();
            _increment = 32;
            _roundLimit = 32;
            _roundShift = 3;
        }

        public PureProjectionReader<TState> Setup(int increment = 32, int roundLimit = 32, int roundShift = 3)
        {
            _increment = increment;
            _roundLimit = roundLimit;
            _roundShift = roundShift;
            return this;
        }

        public void Get(string partition, Action<TState> onLoaded, Action<Exception> onError)
        {
            Item item = GetItem(partition);
            if (item.IsLoaded)
                _executor.Enqueue(new GetStateFinished(onLoaded, item.State));
            else
            {
                bool loadDocument = false;
                lock (item)
                {
                    if (item.IsLoaded)
                        _executor.Enqueue(new GetStateFinished(onLoaded, item.State));
                    else if (item.IsLoading)
                    {
                        item.OnLoadWaiters.Add(onLoaded);
                        item.OnErrorWaiters.Add(onError);
                    }
                    else
                    {
                        item.OnLoadWaiters = new List<Action<TState>>();
                        item.OnErrorWaiters = new List<Action<Exception>>();
                        item.OnLoadWaiters.Add(onLoaded);
                        item.OnErrorWaiters.Add(onError);
                        item.IsLoading = loadDocument = true;
                    }
                }
                if (loadDocument)
                    _store.GetDocument(partition, item.OnDocumentFound, item.OnDocumentMissing, item.OnError);
            }
        }

        private Item GetItem(string partition)
        {
            var item = _cache.GetOrAdd(partition, new Item(this, partition));
            item.NotifyUsage();
            return item;
        }

        public void EndCycle()
        {
            foreach (var item in _cache.Values.ToList())
            {
                item.EndCycle();
                if (item.EvictIfNeeded())
                {
                    Item removed;
                    if (_cache.TryRemove(item.Partition, out removed))
                        removed.NotifyRemoval();
                }
            }
        }
    }
}
