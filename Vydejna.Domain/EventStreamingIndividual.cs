using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class EventStreamingIndividual : IEventStreaming
    {
        private IEventStoreWaitable _store;
        private ITypeMapper _typeMapper;
        private Func<int, int> _delayForRetry;

        public EventStreamingIndividual(IEventStoreWaitable store, ITypeMapper typeMapper)
        {
            _store = store;
            _typeMapper = typeMapper;
        }

        public EventStreamingIndividual SetupWaiting(Func<int, int> delayForRetry)
        {
            _delayForRetry = delayForRetry;
            return this;
        }

        public IEventStreamingInstance GetStreamer(IEnumerable<Type> filter, EventStoreToken token, bool rebuildMode)
        {
            var filterByName = new HashSet<string>(filter.Select<Type, string>(_typeMapper.GetName));
            return new EventsStream(this, filterByName, token, rebuildMode);
        }

        private class EventsStream : IEventStreamingInstance
        {
            private EventStreamingIndividual _parent;
            private readonly IEventStoreWaitable _store;
            private readonly HashSet<string> _filter;
            private EventStoreToken _token;
            private bool _rebuildMode;
            private readonly Queue<EventStoreEvent> _readyEvents;

            public EventsStream(EventStreamingIndividual parent, HashSet<string> filter, EventStoreToken token, bool rebuildMode)
            {
                _parent = parent;
                _store = _parent._store;
                _filter = filter;
                _token = token;
                _rebuildMode = rebuildMode;
                _readyEvents = new Queue<EventStoreEvent>();
            }

            public async Task<EventStoreEvent> GetNextEvent(CancellationToken cancel)
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (_readyEvents.Count > 0)
                        return _readyEvents.Dequeue();
                    var collection = await _store.GetAllEvents(_token, 100, false);
                    _token = collection.NextToken;
                    if (collection.Events.Count > 0)
                    {
                        var usable = collection.Events.Where(e => _filter.Contains(e.Type)).ToList();
                        await _store.LoadBodies(usable);
                        usable.ForEach(_readyEvents.Enqueue);
                    }
                    else if (_rebuildMode)
                    {
                        _rebuildMode = false;
                        return null;
                    }
                    else
                    {
                        await _store.WaitForEvents(_token, cancel);
                    }
                }
            }
        }
    }
}
