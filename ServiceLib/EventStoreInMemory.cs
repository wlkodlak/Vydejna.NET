using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class EventStoreInMemory : IEventStoreWaitable
    {
        private ReaderWriterLockSlim _lock;
        private List<EventStoreEvent> _events;
        private Dictionary<string, int> _versions;
        private Dictionary<string, EventStoreSnapshot> _snapshots;
        private List<Waiter> _waiters;

        public EventStoreInMemory()
        {
            _lock = new ReaderWriterLockSlim();
            _events = new List<EventStoreEvent>();
            _versions = new Dictionary<string, int>();
            _waiters = new List<Waiter>();
            _snapshots = new Dictionary<string, EventStoreSnapshot>();
        }

        private EventStoreToken CreateToken(int eventIndex)
        {
            return new EventStoreToken(eventIndex.ToString());
        }

        public Task<bool> AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            try
            {
                bool wasCompleted;

                try
                {
                    _lock.EnterUpgradeableReadLock();
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    wasCompleted = expectedVersion.VerifyVersion(streamVersion);
                    if (wasCompleted)
                    {
                        var newEvents = events.ToList();
                        var token = _events.Count;
                        foreach (var evt in newEvents)
                        {
                            token++;
                            evt.StreamName = stream;
                            evt.StreamVersion = ++streamVersion;
                            evt.Token = CreateToken(token);
                        }

                        try
                        {
                            _lock.EnterWriteLock();
                            _versions[stream] = streamVersion;
                            _events.AddRange(newEvents);
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }
                    }
                }
                finally
                {
                    _lock.ExitUpgradeableReadLock();
                }
                if (wasCompleted)
                    NotifyWaiters();
                return TaskUtils.FromResult(wasCompleted);
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<bool>(ex);
            }
        }

        public Task<IEventStoreStream> ReadStream(string stream, int minVersion, int maxCount, bool loadBody)
        {
            try
            {
                EventStoreStream result;
                try
                {
                    _lock.EnterReadLock();
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    var list = _events.Where(e => e.StreamName == stream && e.StreamVersion >= minVersion).Take(maxCount).ToList();
                    result = new EventStoreStream(list, streamVersion, 0);
                }
                finally
                {
                    _lock.ExitReadLock();
                }
                return TaskUtils.FromResult<IEventStoreStream>(result);
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<IEventStoreStream>(ex);
            }
        }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody)
        {
            Waiter waiter;
            try
            {
                try
                {
                    _lock.EnterReadLock();
                    waiter = new Waiter(this, token, maxCount, loadBody, CancellationToken.None);
                    if (!waiter.Prepare())
                        waiter.PrepareNowait();
                }
                finally
                {
                    _lock.ExitReadLock();
                }
                waiter.Complete();
                return waiter.Task;
            }
            catch (Exception exception)
            {
                return TaskUtils.FromError<IEventStoreCollection>(exception);
            }
        }

        public Task LoadBodies(IList<EventStoreEvent> events)
        {
            return TaskUtils.CompletedTask();
        }

        public Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
        {
            Waiter waiter;
            bool wasPrepared;
            try
            {
                try
                {
                    _lock.EnterUpgradeableReadLock();
                    waiter = new Waiter(this, token, maxCount, loadBody, cancel);
                    wasPrepared = waiter.Prepare();
                    if (!wasPrepared)
                    {
                        try
                        {
                            _lock.EnterWriteLock();
                            _waiters.Add(waiter);
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }
                    }
                }
                finally
                {
                    _lock.ExitUpgradeableReadLock();
                }
                if (wasPrepared)
                    waiter.Complete();
                return waiter.Task;
            }
            catch (Exception exception)
            {
                return TaskUtils.FromError<IEventStoreCollection>(exception);
            }
        }

        private void NotifyWaiters()
        {
            var readyWaiters = new List<Waiter>();
            try
            {
                _lock.EnterUpgradeableReadLock();
                foreach (var waiter in _waiters)
                {
                    if (waiter.Prepare())
                        readyWaiters.Add(waiter);
                }
                if (readyWaiters.Count == 0)
                    return;
                try
                {
                    _lock.EnterWriteLock();
                    foreach (var waiter in readyWaiters)
                        _waiters.Remove(waiter);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
            foreach (var waiter in readyWaiters)
                waiter.Complete();
        }

        private class Waiter
        {
            private EventStoreInMemory _parent;
            private int _skip;
            private EventStoreToken _token;
            private int _maxCount;
            private bool _loadBody;
            private CancellationToken _cancel;
            private TaskCompletionSource<IEventStoreCollection> _task;
            private List<EventStoreEvent> _readyEvents;
            private EventStoreToken _nextToken;

            public Task<IEventStoreCollection> Task { get { return _task.Task; } }

            public Waiter(EventStoreInMemory parent, EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
            {
                _parent = parent;
                _token = token;
                if (_token.IsInitial)
                    _skip = 0;
                else if (_token.IsCurrent)
                    _skip = _parent._events.Count + 1;
                else
                    _skip = int.Parse(token.ToString());
                _maxCount = maxCount;
                _loadBody = loadBody;
                _cancel = cancel;
                _task = new TaskCompletionSource<IEventStoreCollection>();
                _readyEvents = new List<EventStoreEvent>();
                if (_cancel.CanBeCanceled)
                {
                    cancel.Register(Unregister);
                }
            }

            private void Unregister()
            {
                try
                {
                    _parent._lock.EnterWriteLock();
                    _parent._waiters.Remove(this);
                }
                finally
                {
                    _parent._lock.ExitWriteLock();
                }
                _task.TrySetResult(new EventStoreCollection(new EventStoreEvent[0], _token));
            }

            public bool Prepare()
            {
                if (_parent._events.Count == 0)
                    return false;
                else if (_skip >= _parent._events.Count)
                    return false;
                else if (_maxCount <= 0)
                {
                    _nextToken = _parent.CreateToken(_parent._events.Count);
                    return true;
                }
                else
                {
                    int counter = _maxCount;
                    for (int idx = _skip; idx < _parent._events.Count && counter > 0; idx++)
                    {
                        var evt = _parent._events[idx];
                        _readyEvents.Add(evt);
                        counter--;
                        _nextToken = evt.Token;
                    }
                    return true;
                }
            }

            public void PrepareNowait()
            {
                int totalEvents = _parent._events.Count;
                _nextToken = totalEvents == 0 ? EventStoreToken.Initial : _parent.CreateToken(totalEvents);
            }

            public void Complete()
            {
                _task.TrySetResult(new EventStoreCollection(_readyEvents, _nextToken));
            }
        }

        public Task<EventStoreSnapshot> LoadSnapshot(string stream)
        {
            EventStoreSnapshot snapshot;
            _snapshots.TryGetValue(stream, out snapshot);
            return TaskUtils.FromResult(snapshot);
        }

        public Task SaveSnapshot(string stream, EventStoreSnapshot snapshot)
        {
            snapshot.StreamName = stream;
            _snapshots[stream] = snapshot;
            return TaskUtils.CompletedTask();
        }
    }
}
