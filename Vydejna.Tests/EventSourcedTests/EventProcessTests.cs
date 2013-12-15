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
    public class EventProcessTests : EventStreamingTestBase
    {
        private TestConsumer _consumer;
        private Task _processTask;
        private EventProcess _process;

        [TestInitialize]
        public override void Initialize()
        {
            base.Initialize();
            _consumer = new TestConsumer();
        }

        protected override string ConsumerNameForMetadata()
        {
            return "TestConsumer";
        }

        [TestMethod]
        public void EmptyStore()
        {
            Start();
            Finish();
        }

        [TestMethod]
        public void ProcessFromStart()
        {
            AddEvent("TestEvent1", "ev1");
            AddEvent("TestEvent2", "ev2");
            AddEvent("TestEvent2", "ev3");
            AddEvent("TestEventX", "ev4");
            AddEvent("TestEvent1", "ev5");
            Start();
            Finish();
            ExpectToken(Events[4].Token);
            ExpectContents("ev1\r\nev2\r\nev3\r\nev5\r\n");
        }

        [TestMethod]
        public void ProcessFromMiddle()
        {
            AddEvent("TestEvent1", "ev1");
            AddEvent("TestEvent2", "ev2");
            AddEvent("TestEvent2", "ev3");
            AddEvent("TestEventX", "ev4");
            AddEvent("TestEvent1", "ev5");
            ConsumerMetadata.SetToken(Events[1].Token);
            Start();
            Finish();
            ExpectToken(Events[4].Token);
            ExpectContents("ev3\r\nev5\r\n");
        }

        [TestMethod]
        public void NewEventsAfterAPause()
        {
            AddEvent("TestEvent1", "ev1");
            AddEvent("TestEvent2", "ev2");
            AddEvent("TestEvent2", "ev3");
            AddEvent("TestEventX", "ev4");
            AddEvent("TestEvent1", "ev5");
            ConsumerMetadata.SetToken(Events[1].Token);
            Start();
            AddEvent("TestEvent1", "ev6");
            SignalMoreEvents();
            Finish();
            ExpectToken(Events[5].Token);
            ExpectContents("ev3\r\nev5\r\nev6\r\n");
        }

        private void Start()
        {
            _process = new EventProcess(Streamer, MetadataMgr, new TestSerializer());
            _process.Setup(_consumer);
            _process.Register<TestEvent1>(_consumer);
            _process.Register<TestEvent2>(_consumer);
            _process.Start();
            EventStoreWaits.Wait(1000);
        }

        private void Finish()
        {
            _process.Stop();
        }

        private void ExpectToken(EventStoreToken expected)
        {
            var actual = ConsumerMetadata.GetToken();
            Assert.AreEqual(expected, actual, "Token");
        }

        private void ExpectContents(string expected)
        {
            var actual = _consumer.Results;
            Assert.AreEqual(expected, actual, "Contents");
        }

        private class TestConsumer : IEventConsumer, IHandle<TestEvent1>, IHandle<TestEvent2>
        {
            private StringBuilder _builder = new StringBuilder();

            public string Results { get { return _builder.ToString(); } }

            public string GetConsumerName()
            {
                return "TestConsumer";
            }

            public Task HandleShutdown()
            {
                return TaskResult.GetCompletedTask();
            }

            public Task Handle(TestEvent1 message)
            {
                _builder.AppendFormat("{0}\r\n", message.Data);
                return TaskResult.GetCompletedTask();
            }

            public Task Handle(TestEvent2 message)
            {
                _builder.AppendFormat("{0}\r\n", message.Data);
                return TaskResult.GetCompletedTask();
            }
        }

        private class TestEvent1
        {
            public string Data;
        }
        private class TestEvent2
        {
            public string Data;
        }

        private class TestSerializer : IEventSourcedSerializer
        {
            public bool HandlesFormat(string format)
            {
                return format == "text";
            }

            public object Deserialize(EventStoreEvent evt)
            {
                switch (evt.Type)
                {
                    case "TestEvent1":
                        return new TestEvent1 { Data = evt.Body };
                    case "TestEvent2":
                        return new TestEvent2 { Data = evt.Body };
                    default:
                        throw new InvalidOperationException();
                }
            }

            public void Serialize(object evt, EventStoreEvent stored)
            {
                throw new NotSupportedException();
            }
        }
    }
}
