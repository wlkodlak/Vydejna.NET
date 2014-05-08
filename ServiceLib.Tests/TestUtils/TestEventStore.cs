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
        private Dictionary<string, EventStoreSnapshot> _snapshots;
        private List<Waiter> _waiters;
        private List<string> _streamingLog;

        public TestEventStore(IQueueExecution executor)
        {
            _executor = executor;
            _lock = new object();
            _allEvents = new List<EventStoreEvent>();
            _waiters = new List<Waiter>();
            _streamingLog = new List<string>();
            _snapshots = new Dictionary<string, EventStoreSnapshot>();
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

        public void AddSnapshot(string stream, EventStoreSnapshot snapshot)
        {
            lock (_lock)
            {
                snapshot.StreamName = stream;
                _snapshots[stream] = snapshot;
            }
        }

        public void EndLongPoll()
        {
            NotifyWaiters(new EventStoreEvent[0]);
        }

        public void SendFailure()
        {
            var waiters = _waiters.ToList();
            _waiters.Clear();
            var exception = new Exception("Simulated failure");
            waiters.ForEach(w => w.SendError(exception));
        }

        public void SendTransientFailure()
        {
            var waiters = _waiters.ToList();
            _waiters.Clear();
            var exception = new TransientErrorException("DBOPEN", "Simulated transient failure");
            waiters.ForEach(w => w.SendError(exception));
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
            _streamingLog.AddRange(events.Select(e => "Body " + e.Token.ToString()));
            _executor.Enqueue(onComplete);
        }

        public IList<string> GetStreamingLog() { return _streamingLog; }
        public void ClearStreamingLog() { _streamingLog.Clear(); }

        public void GetAllEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                _streamingLog.Add(string.Format("Get {0} from {1}", maxCount, token.ToString()));
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

        public IList<EventStoreSnapshot> GetAllSnapshots()
        {
            lock (_lock)
                return _snapshots.Values.ToList();
        }

        public IDisposable WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, Action<IEventStoreCollection> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                _streamingLog.Add(string.Format("Wait {0} from {1}", maxCount, token.ToString()));
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
                    waiter = new Waiter(maxCount, token, onComplete, onError, _executor);
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
            private EventStoreToken _token;
            private Action<IEventStoreCollection> _onComplete;
            private Action<Exception> _onError;
            private bool _isDisposed;
            private IQueueExecution _executor;

            public Waiter(int maxCount, EventStoreToken token, Action<IEventStoreCollection> onComplete, Action<Exception> onError, IQueueExecution executor)
            {
                _maxCount = maxCount;
                _token = token;
                _onComplete = onComplete;
                _onError = onError;
                _executor = executor;
            }

            public void NotifyEvents(IEnumerable<EventStoreEvent> events)
            {
                if (_isDisposed)
                    return;
                var selectedEvents = events.Take(_maxCount).ToList();
                if (selectedEvents.Count != 0)
                    _token = selectedEvents.Last().Token;
                _executor.Enqueue(new EventStoreGetAllEventsComplete(_onComplete, selectedEvents, _token));
            }

            public void SendError(Exception exception)
            {
                _executor.Enqueue(_onError, exception);
            }

            public void Dispose()
            {
                _isDisposed = true;
            }
        }

        public void LoadSnapshot(string stream, Action<EventStoreSnapshot> onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                EventStoreSnapshot snapshot;
                _snapshots.TryGetValue(stream, out snapshot);
                onComplete(snapshot);
            }
        }

        public void SaveSnapshot(string stream, EventStoreSnapshot snapshot, Action onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                snapshot.StreamName = stream;
                _snapshots[stream] = snapshot;
                onComplete();
            }
        }
    }
}
