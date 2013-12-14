using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.EventSourcedTests
{
    [TestClass]
    public abstract class EventStreamingTestBase
    {
        private Mock<IProjectionMetadataManager> _metadataMgr;
        private List<ProjectionInstanceMetadata> _allProjectionMetadata;
        private TestStreamer _streamer;
        private TestProjectionMetadata _projectionMetadata;
        private TestConsumerMetadata _consumerMetadata;
        private List<EventStoreEvent> _events;
        private ManualResetEventSlim _eventStoreWaits;
        private List<TaskCompletionSource<object>> _waitersForEvents = new List<TaskCompletionSource<object>>();

        protected TestProjectionMetadata ProjectionMetadata { get { return _projectionMetadata; } }
        protected TestConsumerMetadata ConsumerMetadata { get { return _consumerMetadata; } }
        protected List<EventStoreEvent> Events { get { return _events; } }
        protected IProjectionMetadataManager MetadataMgr { get { return _metadataMgr.Object; } }
        protected TestStreamer Streamer { get { return _streamer; } }
        protected ManualResetEventSlim EventStoreWaits { get { return _eventStoreWaits; } }

        [TestInitialize]
        public virtual void Initialize()
        {
            _events = new List<EventStoreEvent>();
            _streamer = new TestStreamer(_events, WaitForExit);
            _metadataMgr = new Mock<IProjectionMetadataManager>();
            _allProjectionMetadata = new List<ProjectionInstanceMetadata>();
            _projectionMetadata = new TestProjectionMetadata(_allProjectionMetadata);
            _consumerMetadata = new TestConsumerMetadata();
            _eventStoreWaits = new ManualResetEventSlim();

            _metadataMgr
                .Setup(m => m.GetHandler(ConsumerNameForMetadata()))
                .Returns(() => TaskResult.GetCompletedTask<IEventsConsumerMetadata>(_consumerMetadata));
            _metadataMgr
                .Setup(m => m.GetProjection(ConsumerNameForMetadata()))
                .Returns(() => TaskResult.GetCompletedTask<IProjectionMetadata>(_projectionMetadata));
        }

        protected virtual string ConsumerNameForMetadata()
        {
            return "TestConsumer";
        }

        protected Task WaitForExit(CancellationToken token)
        {
            lock (_waitersForEvents)
            {
                var tcs = new TaskCompletionSource<object>();
                _waitersForEvents.Add(tcs);
                token.Register(() => tcs.TrySetCanceled());
                _eventStoreWaits.Set();
                return tcs.Task;
            }
        }

        protected void SignalMoreEvents()
        {
            List<TaskCompletionSource<object>> copy;
            lock (_waitersForEvents)
            {
                copy = _waitersForEvents.ToList();
                _waitersForEvents.Clear();
                _eventStoreWaits.Reset();
            }
            foreach (var item in copy)
                item.TrySetResult(null);
        }

        protected void AddEvent(string typeName, string data)
        {
            var evt = new EventStoreEvent();
            evt.Body = data;
            evt.Format = "text";
            evt.StreamName = "ALL";
            evt.StreamVersion = _events.Count;
            evt.Token = new EventStoreToken(_events.Count.ToString("00000000"));
            evt.Type = typeName;
            _events.Add(evt);
        }

        protected void AddEventPause()
        {
            _events.Add(null);
        }

    }
}
