using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IMemoryCache<T>
    {
        void Get(string key, Action<int, T> onLoaded, Action<Exception> onError, Action<IMemoryCacheLoad<T>> onLoading);
        void Evict(string key);
        void EvictOldEntries(int ticks);
    }

    public interface IMemoryCacheLoad<T>
    {
        string Key { get; }
        int OldVersion { get; }
        T OldValue { get; }
        int Validity { get; set; }
        int Expiration { get; set; }
        void SetLoadedValue(int version, T value);
        void ValueIsStillValid();
        void LoadingFailed(Exception exception);
        IMemoryCacheLoad<T> Expires(int validity, int expiration);
    }

    public class MemoryCacheLoaded<T> : IQueuedExecutionDispatcher
    {
        private readonly Action<int, T> _handler;
        private readonly int _version;
        private readonly T _value;

        public MemoryCacheLoaded(Action<int, T> handler, int version, T value)
        {
            _handler = handler;
            _version = version;
            _value = value;
        }

        public void Execute()
        {
            _handler(_version, _value);
        }
    }

    public class MemoryCache<T> : IMemoryCache<T>
    {
        private ConcurrentDictionary<string, MemoryCacheItem> _contents;
        private IQueueExecution _executor;

        private int _accessIncrement, _ticksPerRound, _agingShift, _roundLimit;
        private int _defaultExpiration, _defaultValidity;
        private int _ticksCounter;
        private int _evicting;
        private int _maxCacheSize, _cleanedCacheSize, _minScore, _minCacheSize;

        public MemoryCache(IQueueExecution executor)
        {
            _contents = new ConcurrentDictionary<string, MemoryCacheItem>();
            _executor = executor;
            _evicting = 0;
            SetupScoring();
            SetupSizing();
            SetupExpiration();
        }

        public MemoryCache<T> SetupExpiration(int validity = 1, int expiration = 1000)
        {
            _defaultValidity = validity;
            _defaultExpiration = expiration;
            return this;
        }

        public MemoryCache<T> SetupScoring(int accessIncrement = 1 << 24, int roundLimit = 1 << 30, int ticksPerRound = 1000, int agingShift = 4, int minScore = 1 << 8)
        {
            _accessIncrement = accessIncrement;
            _ticksPerRound = ticksPerRound;
            _agingShift = agingShift;
            _roundLimit = roundLimit;
            _minScore = minScore;
            return this;
        }

        public MemoryCache<T> SetupSizing(int maxSize = 1 << 16, int cleanSize = 1 << 15, int minSize = 0)
        {
            _maxCacheSize = maxSize;
            _cleanedCacheSize = cleanSize;
            _minCacheSize = minSize;
            return this;
        }

        public void Get(string key, Action<int, T> onLoaded, Action<Exception> onError, Action<IMemoryCacheLoad<T>> onLoading)
        {
            MemoryCacheItem item;
            if (!_contents.TryGetValue(key, out item))
                item = _contents.GetOrAdd(key, new MemoryCacheItem(this, key));
            bool startLoading = false;
            IQueuedExecutionDispatcher immediateLoaded = null;
            lock (item)
            {
                item.NotifyUsage();
                if (item.ShouldReturnImmediately(onLoading == null))
                    immediateLoaded = new MemoryCacheLoaded<T>(onLoaded, item.OldVersion, item.OldValue);
                else
                {
                    item.AddWaiter(onLoaded, onError);
                    startLoading = item.StartLoading();
                }
            }
            if (_contents.Count > _maxCacheSize)
                EvictionInternal(0, _cleanedCacheSize);
            if (immediateLoaded != null)
                _executor.Enqueue(immediateLoaded);
            if (startLoading)
                onLoading(item);
        }

        public void Evict(string key)
        {
            MemoryCacheItem item;
            _contents.TryRemove(key, out item);
        }

        private void EvictionInternal(int ticks, int maxCount)
        {
            if (Interlocked.CompareExchange(ref _evicting, 1, 0) == 1)
                return;
            try
            {
                MemoryCacheItem removed;
                int minScore = (_contents.Count < _minCacheSize) ? 0 : _minScore;
                int shift = 0;
                if (Interlocked.Add(ref _ticksCounter, ticks) > _ticksPerRound)
                {
                    shift = _agingShift * (_ticksCounter / _ticksPerRound);
                    _ticksCounter = 0;
                }
                foreach (var item in _contents.Values)
                {
                    if (item.Eviction(ticks, shift, minScore))
                        _contents.TryRemove(item.Key, out removed);
                }
                if (_contents.Count > maxCount)
                {
                    var keysToRemove = _contents.Values.OrderByDescending(c => c.Score).Skip(maxCount).Select(c => c.Key).ToList();
                    foreach (var key in keysToRemove)
                        _contents.TryRemove(key, out removed);
                }
            }
            finally
            {
                _evicting = 0;
            }
        }

        public void EvictOldEntries(int ticks)
        {
            EvictionInternal(ticks, _maxCacheSize);
        }

        private struct Waiter
        {
            public Action<int, T> OnLoaded;
            public Action<Exception> OnError;
        }

        private class MemoryCacheItem : IMemoryCacheLoad<T>
        {
            private readonly MemoryCache<T> _parent;
            private readonly string _key;
            private bool _loadInProgress, _hasValue;
            private int _version;
            private int _remainingValidity, _remainingExpiration;
            private int _loadingValidity, _loadingExpiration;
            private T _value;
            private int _roundScore, _totalScore;
            private List<Waiter> _waiters;

            public MemoryCacheItem(MemoryCache<T> parent, string key)
            {
                _parent = parent;
                _key = key;
                _waiters = new List<Waiter>();
                _loadingExpiration = _parent._defaultExpiration;
                _loadingValidity = _parent._defaultValidity;
            }

            public int Score { get { return _totalScore; } }
            public string Key { get { return _key; } }
            public int OldVersion { get { return _version; } }
            public T OldValue { get { return _value; } }

            public int Validity
            {
                get { return _loadingValidity; }
                set { _loadingValidity = value; }
            }

            public int Expiration
            {
                get { return _loadingExpiration; }
                set { _loadingExpiration = value; }
            }

            public void SetLoadedValue(int version, T value)
            {
                lock (this)
                {
                    _loadInProgress = false;
                    _remainingExpiration = _loadingExpiration;
                    _remainingValidity = _loadingValidity;
                    _version = version;
                    _value = value;
                    _hasValue = true;
                    foreach (var waiter in _waiters)
                        _parent._executor.Enqueue(new MemoryCacheLoaded<T>(waiter.OnLoaded, _version, _value));
                    _waiters.Clear();
                }
            }

            public void ValueIsStillValid()
            {
                lock (this)
                {
                    _loadInProgress = false;
                    _remainingExpiration = _loadingExpiration;
                    _remainingValidity = _loadingValidity;
                    foreach (var waiter in _waiters)
                        _parent._executor.Enqueue(new MemoryCacheLoaded<T>(waiter.OnLoaded, _version, _value));
                    _waiters.Clear();
                }
            }

            public void LoadingFailed(Exception exception)
            {
                lock (this)
                {
                    _loadInProgress = false;
                    foreach (var waiter in _waiters)
                        _parent._executor.Enqueue(waiter.OnError, exception);
                    _waiters.Clear();
                }
            }

            public IMemoryCacheLoad<T> Expires(int validity, int expiration)
            {
                _loadingExpiration = expiration;
                _loadingValidity = validity;
                return this;
            }

            public void NotifyUsage()
            {
                var increment = Math.Min(_parent._roundLimit - _roundScore, _parent._accessIncrement);
                _roundScore += increment;
                _totalScore += increment;
            }

            public bool ShouldReturnImmediately(bool nowait)
            {
                if (_remainingExpiration == 0)
                {
                    _version = -1;
                    _value = default(T);
                    return false;
                }
                else
                    return _hasValue && _remainingValidity > 0;
            }

            public void AddWaiter(Action<int, T> onLoaded, Action<Exception> onError)
            {
                _waiters.Add(new Waiter { OnLoaded = onLoaded, OnError = onError });
            }

            public bool StartLoading()
            {
                if (_loadInProgress)
                    return false;
                _loadInProgress = true;
                return true;
            }

            public bool Eviction(int ticks, int shift, int minScore)
            {
                lock (this)
                {
                    _remainingExpiration = _remainingExpiration > ticks ? _remainingExpiration - ticks : 0;
                    _remainingValidity = _remainingValidity > ticks ? _remainingValidity - ticks : 0;
                    if (_remainingExpiration == 0)
                        return true;
                    if (shift > 0)
                    {
                        _totalScore = _totalScore >> shift;
                        _roundScore = 0;
                        if (_totalScore < minScore)
                            return true;
                    }
                    return false;
                }
            }
        }
    }
}
