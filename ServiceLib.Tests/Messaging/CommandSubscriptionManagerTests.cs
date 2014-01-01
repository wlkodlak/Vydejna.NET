using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class CommandSubscriptionManagerTests
    {
        private CommandSubscriptionManager _mgr;
        private List<string> _outputs;
        private TestHandler1 _handler1;
        private TestHandler2 _handler2;

        [TestInitialize]
        public void Initialize()
        {
            _mgr = new CommandSubscriptionManager();
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
            bool completed = false;
            bool hasError = false;

            Assert.IsNotNull(handler, "FindHandler returned null");
            handler.Handle(new TestMessage1 { Data = "Hello" }, () => completed = true, ex => hasError = true);
            Assert.AreEqual("Msg1 H1 Hello", string.Join("\r\n", _outputs));
            Assert.IsTrue(completed, "Completed");
            Assert.IsFalse(hasError, "HasError");
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

        [TestMethod]
        public void RegistrationCanBeRemovedUsingDispose()
        {
            var toRemove = _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);
            toRemove.Dispose();

            var handler = _mgr.FindHandler(typeof(TestMessage1));
            Assert.IsNull(handler, "Handler should have been removed");
        }

        [TestMethod]
        public void RegistrationCanBeRemovedUsingDisposeEvenAfterBeingRetrieved()
        {
            var toRemove = _mgr.Register<TestMessage1>(_handler1);
            _mgr.Register<TestMessage2>(_handler1);
            var handler = _mgr.FindHandler(typeof(TestMessage1));
            toRemove.Dispose();
            handler.Handle(new TestMessage1(), () => { }, ex => { });
            Assert.AreEqual(0, _outputs.Count, "Nothing should have been executed");
        }

        [TestMethod]
        public void RegistrationCanBeReplacedWithAnotherHandler()
        {
            var toReplace = _mgr.Register<TestMessage2>(_handler1);
            toReplace.ReplaceWith(_handler2);
            var handler = _mgr.FindHandler(typeof(TestMessage2));

            Assert.IsNotNull(handler, "FindHandler returned null");
            handler.Handle(new TestMessage2 { Data = "Hello" }, () => { }, ex => { });
            Assert.AreEqual("Msg2 H2 Hello", string.Join("\r\n", _outputs));
        }

        private class TestMessage1
        {
            public string Data;
        }
        private class TestMessage2
        {
            public string Data;
        }
        private class TestHandler1 : IHandle<CommandExecution<TestMessage1>>, IHandle<CommandExecution<TestMessage2>>
        {
            private List<string> _calls;
            public TestHandler1(List<string> calls) { _calls = calls; }
            public void Handle(CommandExecution<TestMessage1> msg) { _calls.Add(string.Format("Msg1 H1 {0}", msg.Command.Data)); msg.OnCompleted(); }
            public void Handle(CommandExecution<TestMessage2> msg) { _calls.Add(string.Format("Msg2 H1 {0}", msg.Command.Data)); msg.OnCompleted(); }
        }
        private class TestHandler2 : IHandle<CommandExecution<TestMessage1>>, IHandle<CommandExecution<TestMessage2>>
        {
            private List<string> _calls;
            public TestHandler2(List<string> calls) { _calls = calls; }
            public void Handle(CommandExecution<TestMessage1> msg) { _calls.Add(string.Format("Msg1 H2 {0}", msg.Command.Data)); msg.OnCompleted(); }
            public void Handle(CommandExecution<TestMessage2> msg) { _calls.Add(string.Format("Msg2 H2 {0}", msg.Command.Data)); msg.OnCompleted(); }
        }
    }
}
