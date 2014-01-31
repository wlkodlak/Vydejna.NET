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
        void Insert(string key, int version, T value, int validity = -1, int expiration = -1);
        void Evict(string key);
        void Invalidate(string key);
    }

    public interface IMemoryCacheLoad<T>
    {
        string Key { get; }
        int OldVersion { get; }
        T OldValue { get; }
        bool OldValueAvailable { get; }
        void SetLoadedValue(int version, T value);
        void ValueIsStillValid();
        void LoadingFailed(Exception exception);
        IMemoryCacheLoad<T> Expires(int validityMs, int expirationMs);
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
        private readonly ConcurrentDictionary<string, MemoryCacheItem> _contents;
        private readonly IQueueExecution _executor;
        private readonly ITime _timeService;

        private int _accessIncrement, _agingShift, _roundLimit;
        private long _defaultExpiration, _defaultValidity, _ticksPerRound;
        private long _lastEvictTime, _nextEvictTime;
        private int _evicting;
        private int _maxCacheSize, _cleanedCacheSize, _minScore, _minCacheSize;

        public MemoryCache(IQueueExecution executor, ITime timeService)
        {
            _contents = new ConcurrentDictionary<string, MemoryCacheItem>();
            _executor = executor;
            _timeService = timeService;
            _evicting = 0;
            _lastEvictTime = GetTime();
            SetupScoring();
            SetupSizing();
            SetupExpiration();
        }

        public MemoryCache<T> SetupExpiration(int validity = 1, int expiration = 60000, int msPerRound = 1000)
        {
            _defaultValidity = validity * 10000L;
            _defaultExpiration = expiration * 10000L;
            _ticksPerRound = msPerRound * 10000L;
            _nextEvictTime = _lastEvictTime + _ticksPerRound;
            return this;
        }

        public MemoryCache<T> SetupScoring(int accessIncrement = 1 << 24, int roundLimit = 1 << 30, int agingShift = 4, int minScore = 1 << 8)
        {
            _accessIncrement = accessIncrement;
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
            var currentTime = GetTime();
            lock (item)
            {
                item.NotifyUsage();
                if (item.ShouldReturnImmediately(onLoading == null, currentTime))
                    immediateLoaded = new MemoryCacheLoaded<T>(onLoaded, item.OldVersion, item.OldValue);
                else
                {
                    item.AddWaiter(onLoaded, onError);
                    startLoading = item.StartLoading();
                }
            }
            if (_contents.Count > _maxCacheSize)
                EvictionInternal(currentTime, _cleanedCacheSize);
            else if (_nextEvictTime <= currentTime)
                EvictionInternal(currentTime, _maxCacheSize);
            if (immediateLoaded != null)
                _executor.Enqueue(immediateLoaded);
            if (startLoading)
                onLoading(item);
        }

        public void Insert(string key, int version, T value, int validity = -1, int expiration = -1)
        {
            MemoryCacheItem item;
            if (!_contents.TryGetValue(key, out item))
                item = _contents.GetOrAdd(key, new MemoryCacheItem(this, key));
            lock (item)
            {
                item.NotifyUsage();
                item.Insert(version, value, validity, expiration);
            }
        }

        public void Evict(string key)
        {
            MemoryCacheItem item;
            _contents.TryRemove(key, out item);
        }

        public void Invalidate(string key)
        {
            MemoryCacheItem item;
            if (!_contents.TryGetValue(key, out item))
                return;
            item.Invalidate();
        }

        private void EvictionInternal(long currentTime, int maxCount)
        {
            if (Interlocked.CompareExchange(ref _evicting, 1, 0) == 1)
                return;
            try
            {
                MemoryCacheItem removed;
                int minScore = (_contents.Count < _minCacheSize) ? 0 : _minScore;
                int shift = 0;
                if (currentTime >= _nextEvictTime)
                {
                    var rounds = (int)((currentTime - _lastEvictTime) / _ticksPerRound);
                    shift = Math.Min(64, rounds *_agingShift);
                    _lastEvictTime = currentTime;
                    _nextEvictTime = currentTime + _ticksPerRound;
                }
                foreach (var item in _contents.Values)
                {
                    if (item.Eviction(currentTime, shift, minScore))
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
            private long _validUntil, _expiresOn;
            private long _loadingValidity, _loadingExpiration;
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
                _version = -1;
            }

            public int Score { get { return _totalScore; } }
            public string Key { get { return _key; } }
            public int OldVersion { get { return _version; } }
            public T OldValue { get { return _value; } }
            public bool OldValueAvailable { get { return _version != -1; } }

            public void SetLoadedValue(int version, T value)
            {
                lock (this)
                {
                    _loadInProgress = false;
                    var currentTime = _parent.GetTime();
                    _expiresOn = currentTime + _loadingExpiration;
                    _validUntil = currentTime + _loadingValidity;
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
                    var currentTime = _parent.GetTime();
                    _expiresOn = currentTime + _loadingExpiration;
                    _validUntil = currentTime + _loadingValidity;
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
                _loadingExpiration = expiration == -1 ? _parent._defaultExpiration : expiration * 10000L;
                _loadingValidity = validity == -1 ? _parent._defaultValidity : validity * 10000L;
                return this;
            }

            public void NotifyUsage()
            {
                var increment = Math.Min(_parent._roundLimit - _roundScore, _parent._accessIncrement);
                _roundScore += increment;
                _totalScore += increment;
            }

            public bool ShouldReturnImmediately(bool nowait, long currentTime)
            {
                if (_expiresOn <= currentTime)
                {
                    _version = -1;
                    _value = default(T);
                    return false;
                }
                else
                    return _hasValue && _validUntil > currentTime;
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

            public bool Eviction(long currentTime, int shift, int minScore)
            {
                lock (this)
                {
                    if (_expiresOn <= currentTime)
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

            public void Invalidate()
            {
                lock (this)
                    _validUntil = 0;
            }

            public void Insert(int version, T value, int validity, int expiration)
            {
                if (_loadInProgress)
                    return;
                Expires(validity, expiration);
                SetLoadedValue(version, value);
            }
        }

        private long GetTime()
        {
            return _timeService.GetUtcTime().Ticks;
        }
    }
}
