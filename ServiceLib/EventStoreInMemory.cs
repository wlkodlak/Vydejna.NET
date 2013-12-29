using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class EventStoreInMemory : IEventStoreWaitable
    {
        private UpdateLock _lock;
        private List<EventStoreEvent> _events;
        private Dictionary<string, int> _versions;
        private List<Waiter> _waiters;
        private IQueueExecution _executor;

        public EventStoreInMemory(IQueueExecution executor)
        {
            _lock = new UpdateLock();
            _events = new List<EventStoreEvent>();
            _versions = new Dictionary<string, int>();
            _waiters = new List<Waiter>();
            _executor = executor;
        }

        private EventStoreToken CreateToken(int eventIndex)
        {
            return new EventStoreToken(eventIndex.ToString());
        }

        public void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
        {
            try
            {
                bool wasCompleted;
                using (_lock.Update())
                {
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    wasCompleted = expectedVersion.Verify(streamVersion, stream);
                    if (wasCompleted)
                    {
                        var newEvents = events.ToList();
                        var token = _events.Count;
                        foreach (var evt in newEvents)
                        {
                            evt.StreamName = stream;
                            evt.StreamVersion = ++streamVersion;
                            evt.Token = CreateToken(token);
                            token++;
                        }

                        _lock.Write();
                        _versions[stream] = streamVersion;
                        _events.AddRange(newEvents);
                    }
                }
                if (wasCompleted)
                {
                    NotifyWaiters();
                    _executor.Enqueue(onComplete);
                }
                else
                    _executor.Enqueue(onConcurrency);
            }
            catch (Exception ex)
            {
                _executor.Enqueue(onError, ex);
            }
        }

        public void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
        {
            try
            {
                EventStoreStream result;
                using (_lock.Read())
                {
                    int streamVersion;
                    _versions.TryGetValue(stream, out streamVersion);
                    var list = _events.Where(e => e.StreamName == stream && e.StreamVersion >= minVersion).Take(maxCount).ToList();
                    result = new EventStoreStream(list, streamVersion, 0);
                }
                _executor.Enqueue(new ReadStreamComplete(onComplete, result));
            }
            catch (Exception ex)
            {
                _executor.Enqueue(onError, ex);
            }
        }

        public void GetAllEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            Waiter waiter;
            try
            {
                using (_lock.Read())
                {
                    waiter = new Waiter(this, token, streamPrefix, eventType, maxCount, loadBody, onComplete, onError);
                    if (!waiter.Prepare())
                        waiter.PrepareNowait();
                }
            }
            catch (Exception exception)
            {
                _executor.Enqueue(onError, exception);
                return;
            }
            waiter.Complete();
        }

        public void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
        {
            _executor.Enqueue(onComplete);
        }

        public IDisposable WaitForEvents(EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            Waiter waiter;
            bool wasPrepared;
            try
            {
                using (_lock.Read())
                {
                    waiter = new Waiter(this, token, streamPrefix, eventType, maxCount, loadBody, onComplete, onError);
                    wasPrepared = waiter.Prepare();
                    if (!wasPrepared)
                    {
                        _lock.Write();
                        _waiters.Add(waiter);
                    }
                }
            }
            catch (Exception exception)
            {
                _executor.Enqueue(onError, exception);
                return null;
            }
            if (wasPrepared)
                waiter.Complete();
            return waiter;
        }

        private void NotifyWaiters()
        {
            var readyWaiters = new List<Waiter>();
            using (_lock.Read())
            {
                foreach (var waiter in _waiters)
                {
                    if (waiter.Prepare())
                        readyWaiters.Add(waiter);
                }
                if (readyWaiters.Count == 0)
                    return;
                _lock.Write();
                foreach (var waiter in readyWaiters)
                    _waiters.Remove(waiter);
            }
            foreach (var waiter in readyWaiters)
                waiter.Complete();
        }

        private class Waiter : IDisposable
        {
            private EventStoreInMemory _parent;
            private int _skip;
            private EventStoreToken _token;
            private string _streamPrefix;
            private string _eventType;
            private int _maxCount;
            private bool _loadBody;
            private Action<IEventStoreCollection> _onComplete;
            private Action<Exception> _onError;
            private List<EventStoreEvent> _readyEvents;
            private EventStoreToken _nextToken;

            public Waiter(EventStoreInMemory parent, EventStoreToken token, string streamPrefix, string eventType, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _token = token;
                if (_token.IsInitial)
                    _skip = 0;
                else if (_token.IsCurrent)
                    _skip = _parent._events.Count + 1;
                else
                    _skip = int.Parse(token.ToString());
                _streamPrefix = streamPrefix;
                _eventType = eventType;
                _maxCount = maxCount;
                _loadBody = loadBody;
                _onComplete = onComplete;
                _onError = onError;
                _readyEvents = new List<EventStoreEvent>();
            }

            public void Dispose()
            {
                using (_parent._lock.Lock())
                    _parent._waiters.Remove(this);
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
                        if (evt.StreamName.StartsWith(_streamPrefix ?? "") && (_eventType == null || evt.Type == _eventType))
                        {
                            _readyEvents.Add(evt);
                            counter--;
                        }
                        _nextToken = evt.Token;
                    }
                    return true;
                }
            }

            public void PrepareNowait()
            {
                _nextToken = _parent.CreateToken(_parent._events.Count);
            }

            public void Complete()
            {
                _parent._executor.Enqueue(new GetAllEventsComplete(_onComplete, _readyEvents, _nextToken));
            }
        }
    }
}
