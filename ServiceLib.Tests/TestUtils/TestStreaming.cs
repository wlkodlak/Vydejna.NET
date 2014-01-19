using System;
using System.Linq;
using System.Collections.Generic;

namespace ServiceLib.Tests.TestUtils
{
    public class TestStreaming : IEventStreamingDeserialized
    {
        private IQueueExecution _executor;
        private List<Tuple<string, object>> _events;
        private bool _hasEnd;
        private HashSet<Type> _types;
        private int _position;
        public EventStoreToken CurrentToken;
        public bool IsReading, IsWaiting, IsDisposed;
        private Action<EventStoreToken, object> _onEventRead;
        private Action _onEventNotAvailable;
        private Action<Exception, EventStoreEvent> _onError;
        private bool _nowait;
        public List<string> DeadLetters;
        private string ProcessName;

        private class EventReadCompleted : IQueuedExecutionDispatcher
        {
            private Action<EventStoreToken, object> _handler;
            private EventStoreToken _token;
            private object _event;

            public EventReadCompleted(Action<EventStoreToken, object> handler, EventStoreToken token, object evnt)
            {
                _handler = handler;
                _token = token;
                _event = evnt;
            }

            public void Execute()
            {
                _handler(_token, _event);
            }
        }

        public IList<string> SupportedTypes()
        {
            return _types.Select(t => t.Name).OrderBy(n => n).ToList();
        }

        public TestStreaming(IQueueExecution executor)
        {
            _executor = executor;
            _events = new List<Tuple<string, object>>();
            DeadLetters = new List<string>();
        }

        public void MarkEndOfStream()
        {
            _hasEnd = true;
            TryProcess();
        }

        public void AddEvent(string token, object evnt)
        {
            _hasEnd = false;
            _events.Add(new Tuple<string, object>(token, evnt));
            TryProcess();
        }

        public void MarkAsDeadLetter(Action onComplete, Action<Exception> onError)
        {
            DeadLetters.Add(_events[_position - 1].Item1);
        }

        public void Setup(EventStoreToken firstToken, IList<Type> types, string processName)
        {
            ProcessName = processName;
            CurrentToken = firstToken;
            _types = new HashSet<Type>(types);
            if (CurrentToken.IsInitial)
                _position = 0;
            else if (CurrentToken.IsCurrent)
                _position = _events.Count;
            else
            {
                _position = 0;
                var token = firstToken.ToString();
                while (_position < _events.Count && string.Compare(_events[_position].Item1, token) <= 0)
                    _position++;
            }
        }

        public void GetNextEvent(Action<EventStoreToken, object> onEventRead, Action onEventNotAvailable, Action<Exception, EventStoreEvent> onError, bool nowait)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("Streamer is disposed");
            _onEventRead = onEventRead;
            _onEventNotAvailable = onEventNotAvailable;
            _onError = onError;
            _nowait = nowait;
            IsReading = true;
            IsWaiting = !nowait;
            TryProcess();
        }

        private void TryProcess()
        {
            if (_types == null || !IsReading)
                return;
            while (_position < _events.Count)
            {
                var token = new EventStoreToken(_events[_position].Item1);
                var evnt = _events[_position].Item2;
                CurrentToken = token;
                _position++;
                if (_types.Contains(evnt.GetType()))
                {
                    IsReading = IsWaiting = false;
                    _executor.Enqueue(new EventReadCompleted(_onEventRead, token, evnt));
                    return;
                }
            }
            if (_hasEnd && _nowait)
            {
                IsReading = IsWaiting = false;
                _executor.Enqueue(_onEventNotAvailable);
                return;
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
            if (IsReading)
            {
                IsReading = IsWaiting = false;
                _executor.Enqueue(_onEventNotAvailable);
            }
        }
    }
}
