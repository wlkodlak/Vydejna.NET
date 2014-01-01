using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class SubscriptionManagerTests
    {
        private SubscriptionManager _mgr;
        private List<string> _outputs;
        private TestHandler1 _handler1;
        private TestHandler2 _handler2;

        [TestInitialize]
        public void Initialize()
        {
            _mgr = new SubscriptionManager();
            _outputs = new List<string>();
            _handler1 = new TestHandler1(_outputs);
            _handler2 = new TestHandler2(_outputs);
        }

        [TestMethod]
        public void FindHandlersReturnsRegisteredHandlers()
        {
            _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);

            var handlers = _mgr.FindHandlers(typeof(TestMessage1));

            Assert.IsNotNull(handlers, "FindHandlers returned null");
            Assert.AreEqual(1, handlers.Count, "Found count");
            handlers.First().Handle(new TestMessage1 { Data = "Hello" });
            Assert.AreEqual("Msg1 H1 Hello", string.Join("\r\n", _outputs));
        }

        [TestMethod]
        public void GetHandledTypesReturnsDistinctRegisteredTypes()
        {
            _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);
            _mgr.Register<TestMessage1>(_handler2);
            _mgr.Register<TestMessage2>(_handler2);

            var types = _mgr.GetHandledTypes().ToList();
            Assert.AreEqual("TestMessage1, TestMessage2", string.Join(", ", types.Select(t => t.Name)));
        }

        [TestMethod]
        public void AllHandlersForOneTypeWillBeCalled()
        {
            _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);
            _mgr.Register<TestMessage1>(_handler2);
            _mgr.Register<TestMessage2>(_handler2);
            var handlers = _mgr.FindHandlers(typeof(TestMessage1));

            Assert.IsNotNull(handlers, "FindHandlers returned null");
            Assert.AreEqual(2, handlers.Count, "Found count");
            foreach (var handler in handlers)
                handler.Handle(new TestMessage1 { Data = "Hello" });
            Assert.AreEqual("Msg1 H1 Hello\r\nMsg1 H2 Hello", string.Join("\r\n", _outputs));
        }

        [TestMethod]
        public void RegistrationCanBeRemovedUsingDispose()
        {
            var toRemove = _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);
            _mgr.Register<TestMessage1>(_handler2);
            _mgr.Register<TestMessage2>(_handler2);
            toRemove.Dispose();
            var handlers = _mgr.FindHandlers(typeof(TestMessage1));

            Assert.IsNotNull(handlers, "FindHandlers returned null");
            Assert.AreEqual(1, handlers.Count, "Found count");
            foreach (var handler in handlers)
                handler.Handle(new TestMessage1 { Data = "Hello" });
            Assert.AreEqual("Msg1 H2 Hello", string.Join("\r\n", _outputs));
        }

        [TestMethod]
        public void RegistrationCanBeReplacedWithAnotherHandler()
        {
            _mgr.Register<TestMessage1>(_handler1);
            var toReplace = _mgr.Register<TestMessage2>(_handler1);
            toReplace.ReplaceWith(_handler2);
            var handlers = _mgr.FindHandlers(typeof(TestMessage2));

            Assert.IsNotNull(handlers, "FindHandlers returned null");
            Assert.AreEqual(1, handlers.Count, "Found count");
            foreach (var handler in handlers)
                handler.Handle(new TestMessage2 { Data = "Hello" });
            Assert.AreEqual("Msg2 H2 Hello", string.Join("\r\n", _outputs));
        }

        [TestMethod]
        public void FindHandlersReturnsEmptyCollectionWhenTypeIsNotRegistered()
        {
            _mgr.Register<TestMessage1>(_handler1);
            var handlers = _mgr.FindHandlers(typeof(TestMessage2));
            Assert.IsNotNull(handlers, "FindHandlers returned null");
            Assert.AreEqual(0, handlers.Count, "Found count");
        }

        private class TestMessage1
        {
            public string Data;
        }
        private class TestMessage2
        {
            public string Data;
        }
        private class TestHandler1 : IHandle<TestMessage1>, IHandle<TestMessage2>
        {
            private List<string> _calls;
            public TestHandler1(List<string> calls) { _calls = calls; }
            public void Handle(TestMessage1 msg) { _calls.Add(string.Format("Msg1 H1 {0}", msg.Data)); }
            public void Handle(TestMessage2 msg) { _calls.Add(string.Format("Msg2 H1 {0}", msg.Data)); }
        }
        private class TestHandler2 : IHandle<TestMessage1>, IHandle<TestMessage2>
        {
            private List<string> _calls;
            public TestHandler2(List<string> calls) { _calls = calls; }
            public void Handle(TestMessage1 msg) { _calls.Add(string.Format("Msg1 H2 {0}", msg.Data)); }
            public void Handle(TestMessage2 msg) { _calls.Add(string.Format("Msg2 H2 {0}", msg.Data)); }
        }
    }
}
