﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class EventSubscriptionManagerTests
    {
        private EventSubscriptionManager _mgr;
        private List<string> _outputs;
        private TestHandler1 _handler1;
        private TestHandler2 _handler2;

        [TestInitialize]
        public void Initialize()
        {
            _mgr = new EventSubscriptionManager();
            _outputs = new List<string>();
            _handler1 = new TestHandler1(_outputs);
            _handler2 = new TestHandler2(_outputs);
        }

        [TestMethod]
        public void FindHandlerReturnsRegisteredHandler()
        {
            _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);

            var handler = _mgr.FindHandler(typeof(TestMessage1));

            Assert.IsNotNull(handler, "FindHandler returned null");
            var task = handler.Handle(new TestMessage1 { Data = "Hello" });
            Assert.AreEqual("Msg1 H1 Hello", string.Join("\r\n", _outputs));
            Assert.IsTrue(task.IsCompleted, "Completed");
            Assert.IsNull(task.Exception, "Exception");
        }

        [TestMethod]
        public void CannotRegisterMultipleHandlersForOneType()
        {
            try
            {
                _mgr.Register<TestMessage1>(_handler1);
                _mgr.Register<TestMessage1>(_handler2);
                Assert.Fail("Should not allow registering one type multiple times");
            }
            catch (InvalidOperationException)
            {
            }
        }

        [TestMethod]
        public void GetHandledTypesReturnsBaseRegisteredTypes()
        {
            _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler2);

            var types = _mgr.GetHandledTypes().ToList();
            Assert.AreEqual("TestMessage1, TestMessage2", string.Join(", ", types.Select(t => t.Name)));
        }

        private class TestMessage1
        {
            public string Data = null;
        }
        private class TestMessage2
        {
            public string Data = null;
        }
        private class TestHandler1 : IProcessEvent<TestMessage1>, IProcessEvent<TestMessage2>
        {
            private List<string> _calls;
            public TestHandler1(List<string> calls) { _calls = calls; }
            public Task Handle(TestMessage1 msg) { _calls.Add(string.Format("Msg1 H1 {0}", msg.Data)); return TaskUtils.CompletedTask(); }
            public Task Handle(TestMessage2 msg) { _calls.Add(string.Format("Msg2 H1 {0}", msg.Data)); return TaskUtils.CompletedTask(); }
        }
        private class TestHandler2 : IProcessEvent<TestMessage1>, IProcessEvent<TestMessage2>
        {
            private List<string> _calls;
            public TestHandler2(List<string> calls) { _calls = calls; }
            public Task Handle(TestMessage1 msg) { _calls.Add(string.Format("Msg1 H2 {0}", msg.Data)); return TaskUtils.CompletedTask(); }
            public Task Handle(TestMessage2 msg) { _calls.Add(string.Format("Msg2 H2 {0}", msg.Data)); return TaskUtils.CompletedTask(); }
        }
    }
}
