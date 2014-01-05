using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class PureProjectionDispatcherTests : PureProjectionTestBase
    {
        protected TestHandler _handler;
        protected PureProjectionDispatcher<TestState> _dispatcher;

        [TestInitialize]
        public virtual void Initialize()
        {
            _handler = new TestHandler();
            _dispatcher = new PureProjectionDispatcher<TestState>();
            _dispatcher.Register<TestEvent1>(_handler);
            _dispatcher.Register<TestEvent2>(_handler);
            _dispatcher.Register<TestEvent3>(_handler);
        }

        [TestMethod]
        public void GetRegisteredTypes()
        {
            var expectedTypes = "TestEvent1, TestEvent2, TestEvent3";
            var actualTypes = string.Join(", ", _dispatcher.GetRegisteredTypes().Select(t => t.Name).OrderBy(n => n));
            Assert.AreEqual(expectedTypes, actualTypes);
        }

        [TestMethod]
        public void FindHandler()
        {
            var state = TestState.Initial;
            var evnt = new TestEvent2 { Partition = "A", Data = "42" };
            var impl = _dispatcher.FindHandler(evnt.GetType());
            Assert.IsNotNull(impl, "Handler null");
            Assert.AreEqual(evnt.Partition, impl.Partition(evnt), "Partition");
            var expectedState = new TestState(new EventStoreToken("1"), "E2:42");
            var actualState = impl
                .ApplyEvent(state, evnt, new EventStoreToken("1"))
                .ApplyToken(new EventStoreToken("1"));
            Assert.AreEqual(expectedState, actualState, "State");
        }
    }

    [TestClass]
    public class PureProjectionDispatcherDeduplicationTests : PureProjectionTestBase
    {
        protected TestHandler _handler;
        protected PureProjectionDispatcher<TestState> _dispatcher;
        protected PureProjectionDispatcherDeduplication<TestState> _deduplication;
        protected TestState _state;

        [TestInitialize]
        public virtual void Initialize()
        {
            _state = TestState.Initial;
            _handler = new TestHandler();
            _dispatcher = new PureProjectionDispatcher<TestState>();
            _deduplication = new PureProjectionDispatcherDeduplication<TestState>(_dispatcher, _handler);
            _deduplication.Register<TestEvent1>(_handler);
            _deduplication.Register<TestEvent2>(_handler);
            _deduplication.Register<TestEvent3>(_handler);
        }

        [TestMethod]
        public void GetRegisteredTypes()
        {
            var expectedTypes = "TestEvent1, TestEvent2, TestEvent3";
            var actualTypes = string.Join(", ", _deduplication.GetRegisteredTypes().Select(t => t.Name).OrderBy(n => n));
            Assert.AreEqual(expectedTypes, actualTypes);
        }

        [TestMethod]
        public void GetPartition()
        {
            var evnt = new TestEvent2 { Partition = "A", Data = "42" };
            var impl = _deduplication.FindHandler(evnt.GetType());
            Assert.IsNotNull(impl, "Handler null");
            Assert.AreEqual(evnt.Partition, impl.Partition(evnt), "Partition");
        }

        [TestMethod]
        public void SavesTokenToStateAfterApplyingEvent()
        {
            ApplyEvent<TestEvent2>("1", "A", "42");
            Assert.AreEqual(new TestState("1", "E2:42"), _state);
        }

        [TestMethod]
        public void ProcessEventsWithoutDuplicity()
        {
            ApplyEvent<TestEvent2>("1", "A", "42");
            ApplyEvent<TestEvent1>("2", "A", "58");
            ApplyEvent<TestEvent3>("3", "A", "22");
            ApplyEvent<TestEvent1>("4", "A", "77");
            Assert.AreEqual(new TestState("4", "E2:42 E1:58 E3:22 E1:77"), _state);
        }

        [TestMethod]
        public void ProcessEventsWithDuplicity()
        {
            ApplyEvent<TestEvent2>("1", "A", "42");
            ApplyEvent<TestEvent1>("2", "A", "58");
            ApplyEvent<TestEvent3>("3", "A", "22");
            ApplyEvent<TestEvent1>("2", "A", "58");
            ApplyEvent<TestEvent3>("3", "A", "22");
            ApplyEvent<TestEvent1>("4", "A", "77");
            Assert.AreEqual(new TestState("4", "E2:42 E1:58 E3:22 E1:77"), _state);
        }

        private void ApplyEvent<TEvent>(string tokenBase, string partition, string data)
            where TEvent : TestEvent, new()
        {
            var handler = _deduplication.FindHandler(typeof(TEvent));
            var evnt = new TEvent();
            evnt.Partition = partition;
            evnt.Data = data;
            var token = new EventStoreToken(tokenBase);
            _state = handler.ApplyEvent(_state, evnt, token);
        }
    }
}
