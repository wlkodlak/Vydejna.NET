using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestStreaming : IEventStreamingDeserialized
    {
        public bool IsReading, IsWaiting, IsDisposed;
        public EventStoreToken CurrentToken;
        public List<string> DeadLetters;

        private List<Tuple<string, object>> _events;
        private bool _hasEnd;
        private HashSet<Type> _types;
        private int _position;
        private bool _nowait;
        private string ProcessName;
        private TaskCompletionSource<EventStreamingDeserializedEvent> _tcs;

        public IList<string> SupportedTypes()
        {
            return _types.Select(t => t.Name).OrderBy(n => n).ToList();
        }

        public TestStreaming()
        {
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

        public Task MarkAsDeadLetter()
        {
            DeadLetters.Add(_events[_position - 1].Item1);
            return TaskUtils.CompletedTask();
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

        public Task<EventStreamingDeserializedEvent> GetNextEvent(bool nowait)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("Streamer is disposed");
            _tcs = new TaskCompletionSource<EventStreamingDeserializedEvent>();
            _nowait = nowait;
            IsReading = true;
            IsWaiting = !nowait;
            TryProcess();
            return _tcs.Task;
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
                    _tcs.TrySetResult(new EventStreamingDeserializedEvent(new EventStoreEvent { Token = token }, evnt));
                    return;
                }
            }
            if (_hasEnd && _nowait)
            {
                IsReading = IsWaiting = false;
                _tcs.TrySetResult(null);
                return;
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
            if (IsReading)
            {
                IsReading = IsWaiting = false;
                _tcs.TrySetResult(null);
            }
        }
    }
}
