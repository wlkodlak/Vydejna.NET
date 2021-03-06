﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace ServiceLib
{
    public delegate Task<MemoryCacheItem<T>> MemoryCacheLoadDelegate<T>(IMemoryCacheLoad<T> load);

    public delegate Task<int> MemoryCacheSaveDelegate<T>(IMemoryCacheSave<T> save);

    public interface IMemoryCache<T>
    {
        Task<MemoryCacheItem<T>> Get(string key, MemoryCacheLoadDelegate<T> onLoading);
        void Insert(string key, int version, T value, int validity = -1, int expiration = -1, bool dirty = false);
        void Evict(string key);
        void Invalidate(string key);
        void Clear();
        Task Flush(MemoryCacheSaveDelegate<T> saveAction);
        List<T> GetAllChanges();
    }

    public static class MemoryCacheItem
    {
        public static MemoryCacheItem<T> Create<T>(int version, T value)
        {
            return new MemoryCacheItem<T>(version, value);
        }

        [Obsolete]
        public static MemoryCacheItem<TOut> Transform<TIn, TOut>(
            this MemoryCacheItem<TIn> input, Func<TIn, TOut> tranformer)
        {
            return new MemoryCacheItem<TOut>(input.Version, tranformer(input.Value));
        }

        [Obsolete]
        public static Task<MemoryCacheItem<TOut>> Transform<TIn, TOut>(
            this Task<MemoryCacheItem<TIn>> inputTask, Func<TIn, TOut> tranformer)
        {
            return inputTask.ContinueWith(
                task =>
                {
                    var result = task.Result;
                    if (result != null)
                        return new MemoryCacheItem<TOut>(result.Version, tranformer(result.Value));
                    else
                        return null;
                });
        }

        [Obsolete]
        public static Task<MemoryCacheItem<TOut>> ToMemoryCacheItem<TOut>(
            this Task<DocumentStoreFoundDocument> inputTask, Func<string, TOut> deserializer)
        {
            return inputTask.ContinueWith(
                task =>
                {
                    var result = task.Result;
                    if (result == null)
                        return new MemoryCacheItem<TOut>(0, default(TOut));
                    else if (!result.HasNewerContent)
                        return null;
                    else if (string.IsNullOrEmpty(result.Contents))
                        return new MemoryCacheItem<TOut>(result.Version, default(TOut));
                    else
                        return new MemoryCacheItem<TOut>(result.Version, deserializer(result.Contents));
                });
        }

        [Obsolete]
        public static Task<T> ExtractValue<T>(this Task<MemoryCacheItem<T>> inputTask)
        {
            return inputTask.ContinueWith(task => task.Result == null ? default(T) : task.Result.Value);
        }
    }

    public class MemoryCacheItem<T>
    {
        public readonly int Version;
        public readonly T Value;

        public MemoryCacheItem(int version, T value)
        {
            Version = version;
            Value = value;
        }
    }

    public interface IMemoryCacheLoad<T>
    {
        string Key { get; }
        int OldVersion { get; }
        T OldValue { get; }
        bool OldValueAvailable { get; }
        void Expires(int validity, int expiration);
    }

    public interface IMemoryCacheSave<T>
    {
        string Key { get; }
        int Version { get; }
        T Value { get; }
    }

    public class MemoryCache<T> : IMemoryCache<T>
    {
        private readonly ConcurrentDictionary<string, InternalItem> _contents;
        private readonly ITime _timeService;

        private int _accessIncrement, _agingShift, _roundLimit;
        private long _defaultExpiration, _defaultValidity, _ticksPerRound;
        private long _lastEvictTime, _nextEvictTime;
        private int _evicting;
        private int _maxCacheSize, _cleanedCacheSize, _minScore, _minCacheSize;

        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.MemoryCache<" + typeof (T).Name + ">");

        public MemoryCache(ITime timeService)
        {
            _contents = new ConcurrentDictionary<string, InternalItem>();
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

        public MemoryCache<T> SetupScoring(
            int accessIncrement = 1 << 24, int roundLimit = 1 << 30, int agingShift = 4, int minScore = 1 << 8)
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

        public Task<MemoryCacheItem<T>> Get(string key, MemoryCacheLoadDelegate<T> onLoading)
        {
            InternalItem item;
            if (!_contents.TryGetValue(key, out item))
                item = _contents.GetOrAdd(key, new InternalItem(this, key));
            bool startLoading = false;
            var currentTime = GetTime();
            Task<MemoryCacheItem<T>> resultTask;
            lock (item)
            {
                item.NotifyUsage();
                if (item.ShouldReturnImmediately(onLoading == null, currentTime))
                {
                    Logger.DebugFormat("Get({0}): valid at version {1}", key, item.OldVersion);
                    resultTask = TaskUtils.FromResult(MemoryCacheItem.Create(item.OldVersion, item.OldValue));
                }
                else
                {
                    resultTask = item.AddLoadingWaiter();
                    startLoading = item.StartLoading();
                }
            }
            if (_contents.Count > _maxCacheSize)
                EvictionInternal(currentTime, _cleanedCacheSize);
            else if (_nextEvictTime <= currentTime)
                EvictionInternal(currentTime, _maxCacheSize);
            if (startLoading && onLoading != null)
            {
                onLoading(item).ContinueWith(item.LoadingFinished);
            }
            return resultTask;
        }

        public void Insert(string key, int version, T value, int validity = -1, int expiration = -1, bool dirty = false)
        {
            InternalItem item;
            if (!_contents.TryGetValue(key, out item))
                item = _contents.GetOrAdd(key, new InternalItem(this, key));
            lock (item)
            {
                Logger.DebugFormat("Insert({0}, version: {1})", key, version);
                item.NotifyUsage();
                item.Insert(version, value, validity, expiration, dirty);
            }
        }

        public void Evict(string key)
        {
            InternalItem item;
            Logger.DebugFormat("Evict({0})", key);
            _contents.TryRemove(key, out item);
        }

        public void Invalidate(string key)
        {
            InternalItem item;
            if (!_contents.TryGetValue(key, out item))
                return;
            Logger.DebugFormat("Invalidate({0})", key);
            item.Invalidate();
        }

        private void EvictionInternal(long currentTime, int maxCount)
        {
            if (Interlocked.CompareExchange(ref _evicting, 1, 0) == 1)
                return;
            try
            {
                InternalItem removed;
                int minScore = (_contents.Count < _minCacheSize) ? 0 : _minScore;
                int shift = 0;
                if (currentTime >= _nextEvictTime)
                {
                    var rounds = (int) ((currentTime - _lastEvictTime) / _ticksPerRound);
                    shift = Math.Min(64, rounds * _agingShift);
                    _lastEvictTime = currentTime;
                    _nextEvictTime = currentTime + _ticksPerRound;
                }
                int evictedCount = 0;
                foreach (var item in _contents.Values)
                {
                    if (item.Eviction(currentTime, shift, minScore))
                    {
                        _contents.TryRemove(item.Key, out removed);
                        evictedCount++;
                    }
                }
                if (_contents.Count > maxCount)
                {
                    var keysToRemove =
                        _contents.Values.OrderByDescending(c => c.Score).Skip(maxCount).Select(c => c.Key).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _contents.TryRemove(key, out removed);
                        evictedCount++;
                    }
                }
                if (evictedCount > 0)
                    Logger.DebugFormat("Evicted {0} items", evictedCount);
            }
            finally
            {
                _evicting = 0;
            }
        }

        private class InternalItem : IMemoryCacheLoad<T>, IMemoryCacheSave<T>
        {
            private readonly MemoryCache<T> _parent;
            private readonly string _key;
            private bool _ioInProgress, _hasValue, _dirty;
            private int _version;
            private long _validUntil, _expiresOn;
            private long _loadingValidity, _loadingExpiration;
            private T _value;
            private int _roundScore, _totalScore;
            private readonly List<TaskCompletionSource<MemoryCacheItem<T>>> _loadingWaiters;

            public InternalItem(MemoryCache<T> parent, string key)
            {
                _parent = parent;
                _key = key;
                _loadingExpiration = _parent._defaultExpiration;
                _loadingValidity = _parent._defaultValidity;
                _version = -1;
                _loadingWaiters = new List<TaskCompletionSource<MemoryCacheItem<T>>>();
            }

            public int Score
            {
                get { return _totalScore; }
            }

            public string Key
            {
                get { return _key; }
            }

            public int OldVersion
            {
                get { return _version; }
            }

            public T OldValue
            {
                get { return _value; }
            }

            public bool OldValueAvailable
            {
                get { return _version != -1; }
            }

            public bool Dirty
            {
                get { return _dirty; }
            }

            public int Version
            {
                get { return _version; }
            }

            public T Value
            {
                get { return _value; }
            }

            public void Expires(int validity, int expiration)
            {
                _loadingExpiration = expiration * 10000L;
                _loadingValidity = validity * 10000L;
            }

            public void LoadingFinished(Task<MemoryCacheItem<T>> taskLoading)
            {
                IList<TaskCompletionSource<MemoryCacheItem<T>>> waiters;
                if (taskLoading.Exception != null)
                {
                    lock (this)
                    {
                        _ioInProgress = false;
                        waiters = _loadingWaiters.ToList();
                        _loadingWaiters.Clear();
                    }
                    Logger.DebugFormat("Loading of {0} failed, sending errors to {1} waiters", _key, waiters.Count);
                    foreach (var waiter in waiters)
                        waiter.TrySetException(taskLoading.Exception.InnerExceptions);
                }
                else
                {
                    MemoryCacheItem<T> result;
                    lock (this)
                    {
                        _ioInProgress = false;
                        var currentTime = _parent.GetTime();
                        _expiresOn = currentTime + _loadingExpiration;
                        _validUntil = currentTime + _loadingValidity;
                        waiters = _loadingWaiters.ToList();
                        _loadingWaiters.Clear();
                        if (taskLoading.Result != null)
                        {
                            result = taskLoading.Result;
                            _hasValue = true;
                            _version = result.Version;
                            _value = result.Value;
                        }
                        else if (_hasValue)
                        {
                            result = MemoryCacheItem.Create(_version, _value);
                        }
                        else
                        {
                            result = MemoryCacheItem.Create(0, default(T));
                        }
                    }
                    Logger.DebugFormat(
                        "Loading of {0} finished, sending result at version {1} to {2} waiters",
                        _key, _version, waiters.Count);
                    foreach (var waiter in waiters)
                        waiter.TrySetResult(result);
                }
            }

            public void SavingFinished(Task<int> taskSave)
            {
                IList<TaskCompletionSource<MemoryCacheItem<T>>> waiters;
                if (taskSave.Exception != null)
                {
                    lock (this)
                    {
                        lock (this)
                        {
                            _ioInProgress = false;
                            waiters = _loadingWaiters.ToList();
                            _loadingWaiters.Clear();
                        }
                        Logger.DebugFormat("Saving of {0} failed, sending errors to {1} waiters", _key, waiters.Count);
                        foreach (var waiter in waiters)
                            waiter.TrySetException(taskSave.Exception.InnerExceptions);
                    }
                }
                else
                {
                    MemoryCacheItem<T> result;
                    lock (this)
                    {
                        _ioInProgress = false;
                        _dirty = false;
                        var currentTime = _parent.GetTime();
                        _expiresOn = currentTime + _loadingExpiration;
                        _validUntil = currentTime + _loadingValidity;
                        waiters = _loadingWaiters.ToList();
                        _loadingWaiters.Clear();
                        _version = taskSave.Result;
                        result = MemoryCacheItem.Create(_version, _value);
                    }
                    Logger.DebugFormat(
                        "Saving of {0} finished, sending version {1} to {2} waiters",
                        _key, _version, waiters.Count);
                    foreach (var waiter in waiters)
                        waiter.TrySetResult(result);
                }
            }

            public void NotifyUsage()
            {
                var increment = Math.Min(_parent._roundLimit - _roundScore, _parent._accessIncrement);
                _roundScore += increment;
                _totalScore += increment;
            }

            public bool ShouldReturnImmediately(bool nowait, long currentTime)
            {
                if (_dirty)
                    return true;
                else if (_expiresOn <= currentTime)
                {
                    _version = -1;
                    _value = default(T);
                    return nowait;
                }
                else if (_hasValue && _validUntil > currentTime)
                    return true;
                else
                    return nowait;
            }

            public Task<MemoryCacheItem<T>> AddLoadingWaiter()
            {
                var waiter = new TaskCompletionSource<MemoryCacheItem<T>>();
                _loadingWaiters.Add(waiter);
                return waiter.Task;
            }

            public bool StartLoading()
            {
                if (_ioInProgress)
                    return false;
                _ioInProgress = true;
                return true;
            }

            public bool StartSaving()
            {
                if (!_dirty || _ioInProgress)
                    return false;
                _ioInProgress = true;
                return true;
            }

            public bool Eviction(long currentTime, int shift, int minScore)
            {
                lock (this)
                {
                    if (_dirty)
                        return false;
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

            public void Insert(int version, T value, int validity, int expiration, bool dirty)
            {
                if (_ioInProgress)
                    return;
                _loadingExpiration = expiration == -1 ? _parent._defaultExpiration : expiration * 10000L;
                _loadingValidity = validity == -1 ? _parent._defaultValidity : validity * 10000L;
                if (dirty)
                    _dirty = true;
                var currentTime = _parent.GetTime();
                _expiresOn = currentTime + _loadingExpiration;
                _validUntil = currentTime + _loadingValidity;
                _hasValue = true;
                _version = version;
                _value = value;
            }
        }

        private long GetTime()
        {
            return _timeService.GetUtcTime().Ticks;
        }

        public void Clear()
        {
            _contents.Clear();
        }

        public Task Flush(MemoryCacheSaveDelegate<T> saveAction)
        {
            return TaskUtils.FromEnumerable(FlushInternal(saveAction)).GetTask();
        }

        private IEnumerable<Task> FlushInternal(MemoryCacheSaveDelegate<T> saveAction)
        {
            var pendingSaves = new List<InternalItem>();
            foreach (var data in _contents.Values)
            {
                lock (data)
                {
                    if (data.StartSaving())
                        pendingSaves.Add(data);
                }
            }
            foreach (var data in pendingSaves)
            {
                var taskSave = saveAction(data);
                yield return taskSave;
                data.SavingFinished(taskSave);
            }
        }

        public List<T> GetAllChanges()
        {
            var changes = new List<T>();
            foreach (var data in _contents.Values)
            {
                lock (data)
                {
                    if (data.Dirty && data.OldValueAvailable)
                        changes.Add(data.OldValue);
                }
            }
            return changes;
        }
    }
}