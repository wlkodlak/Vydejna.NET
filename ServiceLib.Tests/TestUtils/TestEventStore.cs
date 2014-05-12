using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestEventStore : IEventStoreWaitable
    {
        private object _lock;
        private List<EventStoreEvent> _allEvents;
        private Dictionary<string, EventStoreSnapshot> _snapshots;
        private List<Waiter> _waiters;
        private List<string> _streamingLog;

        public TestEventStore()
        {
            _lock = new object();
            _allEvents = new List<EventStoreEvent>();
            _waiters = new List<Waiter>();
            _streamingLog = new List<string>();
            _snapshots = new Dictionary<string, EventStoreSnapshot>();
        }

        public Task<bool> AddToStream(string stream, IEnumerable<EventStoreEvent> events, EventStoreVersion expectedVersion)
        {
            lock (_lock)
            {
                var version = _allEvents.Where(e => e.StreamName == stream).Count();
                if (!expectedVersion.VerifyVersion(version))
                    return TaskUtils.FromResult(false);
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
                    NotifyWaiters(newEvents);
                    return TaskUtils.FromResult(true);
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

        public Task<IEventStoreStream> ReadStream(string stream, int minVersion, int maxCount, bool loadBody)
        {
            lock (_lock)
            {
                var streamEvents = _allEvents.Where(e => e.StreamName == stream).ToList();
                var version = streamEvents.Count;
                var selectedEvents = streamEvents.Where(e => e.StreamVersion >= minVersion).Take(maxCount).ToList();
                return TaskUtils.FromResult<IEventStoreStream>(new EventStoreStream(selectedEvents, version, 0));
            }
        }

        public List<EventStoreEvent> ReadStream(string stream)
        {
            lock (_lock)
                return _allEvents.Where(e => e.StreamName == stream).ToList();
        }

        public Task LoadBodies(IList<EventStoreEvent> events)
        {
            _streamingLog.AddRange(events.Select(e => "Body " + e.Token.ToString()));
            return TaskUtils.CompletedTask();
        }

        public IList<string> GetStreamingLog() { return _streamingLog; }
        public void ClearStreamingLog() { _streamingLog.Clear(); }

        public Task<IEventStoreCollection> GetAllEvents(EventStoreToken token, int maxCount, bool loadBody)
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
                return TaskUtils.FromResult<IEventStoreCollection>(new EventStoreCollection(events, nextToken));
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

        public Task<IEventStoreCollection> WaitForEvents(EventStoreToken token, int maxCount, bool loadBody, CancellationToken cancel)
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
                    waiter = new Waiter(maxCount, token, cancel);
                if (waiter == null)
                    return TaskUtils.FromResult<IEventStoreCollection>(new EventStoreCollection(events, nextToken));
                else
                {
                    _waiters.Add(waiter);
                    return waiter.Task;
                }
            }
        }

        private class Waiter
        {
            private int _maxCount;
            private EventStoreToken _token;
            private bool _isDisposed;
            private TaskCompletionSource<IEventStoreCollection> _task;
            private CancellationToken _cancel;
            private CancellationTokenRegistration _cancelRegistration;

            public Waiter(int maxCount, EventStoreToken token, CancellationToken cancel)
            {
                _maxCount = maxCount;
                _token = token;
                _cancel = cancel;
                _task = new TaskCompletionSource<IEventStoreCollection>();
                _cancelRegistration = cancel.Register(Dispose);
            }

            public Task<IEventStoreCollection> Task { get { return _task.Task; } }

            public void NotifyEvents(IEnumerable<EventStoreEvent> events)
            {
                if (_isDisposed)
                    return;
                var selectedEvents = events.Take(_maxCount).ToList();
                if (selectedEvents.Count != 0)
                    _token = selectedEvents.Last().Token;
                _cancelRegistration.Dispose();
                _task.TrySetResult(new EventStoreCollection(selectedEvents, _token));
            }

            public void SendError(Exception exception)
            {
                _task.TrySetException(exception);
            }

            private void Dispose()
            {
                _isDisposed = true;
                _task.TrySetCanceled();
            }
        }

        public Task<EventStoreSnapshot> LoadSnapshot(string stream)
        {
            lock (_lock)
            {
                EventStoreSnapshot snapshot;
                _snapshots.TryGetValue(stream, out snapshot);
                return TaskUtils.FromResult(snapshot);
            }
        }

        public Task SaveSnapshot(string stream, EventStoreSnapshot snapshot)
        {
            lock (_lock)
            {
                snapshot.StreamName = stream;
                _snapshots[stream] = snapshot;
                return TaskUtils.CompletedTask();
            }
        }
    }
}
