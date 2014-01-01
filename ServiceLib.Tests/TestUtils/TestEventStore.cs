using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestEventStore : IEventStoreWaitable
    {
        private IQueueExecution _executor;
        private object _lock;
        private List<EventStoreEvent> _allEvents;
        private List<Waiter> _waiters;

        public TestEventStore(IQueueExecution executor)
        {
            _executor = executor;
            _lock = new object();
            _allEvents = new List<EventStoreEvent>();
            _waiters = new List<Waiter>();
        }

        public void AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion, Action onComplete, Action onConcurrency, Action<Exception> onError)
        {
            lock (_lock)
            {
                var version = _allEvents.Where(e => e.StreamName == stream).Count();
                if (!expectedVersion.VerifyVersion(version))
                    _executor.Enqueue(onConcurrency);
                else
                {
                    var newEvents = events.ToList();
                    foreach (var evnt in newEvents)
                    {
                        version++;
                        evnt.StreamName = stream;
                        evnt.StreamVersion = version;
                        evnt.Token = new EventStoreToken((_allEvents.Count + 1).ToString());
                        _allEvents.Add(evnt);
                    }
                    _executor.Enqueue(onComplete);
                    NotifyWaiters(newEvents);
                }
            }
        }

        public void AddToStream(string stream, IList<EventStoreEvent> events)
        {
            lock (_lock)
            {
                var version = _allEvents.Where(e => e.StreamName == stream).Count();
                var newEvents = events.ToList();
                foreach (var evnt in newEvents)
                {
                    version++;
                    evnt.StreamName = stream;
                    evnt.StreamVersion = version;
                    evnt.Token = new EventStoreToken((_allEvents.Count + 1).ToString());
                    _allEvents.Add(evnt);
                }
                NotifyWaiters(newEvents);
            }
        }

        private void NotifyWaiters(IList<EventStoreEvent> newEvents)
        {
            var waiters = _waiters.ToList();
            _waiters.Clear();
            waiters.ForEach(w => w.NotifyEvents(newEvents));
        }

        public void ReadStream(string stream, int minVersion, int maxCount, bool loadBody, Action<IEventStoreStream> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                var streamEvents = _allEvents.Where(e => e.StreamName == stream).ToList();
                var version = streamEvents.Count;
                var selectedEvents = streamEvents.Where(e => e.StreamVersion >= minVersion).Take(maxCount).ToList();
                _executor.Enqueue(new EventStoreReadStreamComplete(onComplete, new EventStoreStream(selectedEvents, version, 0)));
            }
        }

        public List<EventStoreEvent> ReadStream(string stream)
        {
            lock (_lock)
                return _allEvents.Where(e => e.StreamName == stream).ToList();
        }

        public void LoadBodies(IList<EventStoreEvent> events, Action onComplete, Action<Exception> onError)
        {
            _executor.Enqueue(onComplete);
        }

        public void GetAllEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                int skip = token.IsInitial ? 0 : token.IsCurrent ? _allEvents.Count : int.Parse(token.ToString());
                var events = _allEvents.Skip(skip).Take(maxCount).ToList();
                var currentToken = _allEvents.Count == 0 ? EventStoreToken.Initial : _allEvents.Last().Token;
                EventStoreToken nextToken;
                if (events.Count > 0)
                    nextToken = events.Last().Token;
                else if (maxCount == 0 && !token.IsCurrent)
                    nextToken = token;
                else
                    nextToken = currentToken;
                _executor.Enqueue(new EventStoreGetAllEventsComplete(onComplete, events, nextToken));
            }
        }

        public IList<EventStoreEvent> GetAllEvents()
        {
            lock (_lock)
                return _allEvents.ToList();
        }

        public IDisposable WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                int skip = token.IsInitial ? 0 : token.IsCurrent ? _allEvents.Count : int.Parse(token.ToString());
                var events = _allEvents.Skip(skip).Take(maxCount).ToList();
                var currentToken = _allEvents.Count == 0 ? EventStoreToken.Initial : _allEvents.Last().Token;
                Waiter waiter = null;
                EventStoreToken nextToken = null;
                if (events.Count > 0)
                    nextToken = events.Last().Token;
                else if (maxCount == 0)
                    nextToken = token.IsCurrent ? currentToken : token;
                else
                    waiter = new Waiter(maxCount, onComplete, _executor);
                if (waiter == null)
                {
                    _executor.Enqueue(new EventStoreGetAllEventsComplete(onComplete, events, nextToken));
                    return null;
                }
                else
                {
                    _waiters.Add(waiter);
                    return waiter;
                }
            }
        }

        private class Waiter : IDisposable
        {
            private int _maxCount;
            private Action<IEventStoreCollection> _onComplete;
            private bool _isDisposed;
            private IQueueExecution _executor;

            public Waiter(int maxCount, Action<IEventStoreCollection> onComplete, IQueueExecution executor)
            {
                _maxCount = maxCount;
                _onComplete = onComplete;
                _executor = executor;
            }
            
            public void NotifyEvents(IEnumerable<EventStoreEvent> events)
            {
                if (_isDisposed)
                    return;
                var selectedEvents = events.Take(_maxCount).ToList();
                _executor.Enqueue(new EventStoreGetAllEventsComplete(_onComplete, selectedEvents, selectedEvents.Last().Token));
            }

            public void Dispose()
            {
                _isDisposed = true;
            }
        }
    }
}
