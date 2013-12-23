using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.MessagingTests
{
    [TestClass]
    public class SubscriptionManagerTests
    {
        private SubscriptionManager _mgr;
        private TestHandler _handler;
        private List<string> _invocations;

        [TestInitialize]
        public void Initialize()
        {
            _mgr = new SubscriptionManager();
            _invocations = new List<string>();
            _handler = new TestHandler("A", _invocations);
        }

        [TestMethod]
        public void RegisterSingleEntry()
        {
            _mgr.Register<TestMessage1>(_handler);
            HandleMessage(new TestMessage1 { Data = "Test" });
            VerifyInvocations("A TestMessage1: Test");
        }

        [TestMethod]
        public void NoRegistrations()
        {
            HandleMessage(new TestMessage1 { Data = "Test" });
            VerifyInvocations();
        }

        [TestMethod]
        public void MultipleRegistrationsForAType()
        {
            _mgr.Register<TestMessage2>(_handler);
            var handler2 = new TestHandler("B", _invocations);
            _mgr.Register<TestMessage2>(handler2);
            HandleMessage(new TestMessage2 { Data = "Test data" });
            VerifyInvocations(
                "A TestMessage2: Test data",
                "B TestMessage2: Test data");
        }

        [TestMethod]
        public void DifferentTypes()
        {
            _mgr.Register<TestMessage1>(_handler);
            _mgr.Register<TestMessage2>(_handler);
            _mgr.Register<TestMessage3>(_handler);
            HandleMessage(new TestMessage3 { Data = "Test data" });
            VerifyInvocations("A TestMessage3: Test data");
        }

        [TestMethod]
        public void RegistrationDoesNotRunningEnumerations()
        {
            _mgr.Register<TestMessage2>(_handler);
            var message = new TestMessage2 { Data = "Test data" };
            var handlers = _mgr.FindHandlers(typeof(TestMessage2));
            Assert.IsNotNull(handlers, "FindHandlers");
            var enumerator = handlers.GetEnumerator();

            var handler2 = new TestHandler("B", _invocations);
            _mgr.Register<TestMessage2>(handler2);

            while (enumerator.MoveNext())
                enumerator.Current.Handle(message).Wait();
            
            VerifyInvocations("A TestMessage2: Test data");
        }

        [TestMethod]
        public void CanUnregister()
        {
            _mgr.Register<TestMessage1>(_handler);
            _mgr.Register<TestMessage2>(_handler);
            _mgr.Register<TestMessage3>(_handler);
            var handler2 = new TestHandler("B", _invocations);
            _mgr.Register<TestMessage1>(handler2);
            var reg2 = _mgr.Register<TestMessage2>(handler2);
            _mgr.Register<TestMessage3>(handler2);

            HandleMessage(new TestMessage3 { Data = "Data 1" });
            reg2.Dispose();
            HandleMessage(new TestMessage1 { Data = "Data 2" });
            HandleMessage(new TestMessage2 { Data = "Data 3" });

            VerifyInvocations(
                "A TestMessage3: Data 1",
                "B TestMessage3: Data 1",
                "A TestMessage1: Data 2",
                "B TestMessage1: Data 2",
                "A TestMessage2: Data 3");
        }

        [TestMethod]
        public void CanChangeRegistration()
        {
            var reg1 = _mgr.Register<TestMessage1>(_handler);
            var reg2 = _mgr.Register<TestMessage2>(_handler);
            var reg3 = _mgr.Register<TestMessage3>(_handler);
            var handler2 = new TestHandler("B", _invocations);
            reg1.ReplaceWith(handler2);
            reg2.ReplaceWith(handler2);
            reg3.ReplaceWith(handler2);

            HandleMessage(new TestMessage3 { Data = "Data 1" });
            HandleMessage(new TestMessage1 { Data = "Data 2" });
            HandleMessage(new TestMessage2 { Data = "Data 3" });

            VerifyInvocations(
                "B TestMessage3: Data 1",
                "B TestMessage1: Data 2",
                "B TestMessage2: Data 3");
        }

        [TestMethod]
        public void GetHandledTypes()
        {
            var reg1 = _mgr.Register<TestMessage1>(_handler);
            var reg2 = _mgr.Register<TestMessage2>(_handler);
            var reg3 = _mgr.Register<TestMessage3>(_handler);
            var types = _mgr.GetHandledTypes();
            var expected = "TestMessage1, TestMessage2, TestMessage3";
            var actual = string.Join(", ", types.Select(x => x.Name).OrderBy(x => x));
            Assert.AreEqual(expected, actual);

        }

        private void HandleMessage(object message)
        {
            var type = message.GetType();
            var handlers = _mgr.FindHandlers(type);
            Assert.IsNotNull(handlers, "FindHandlers for {0}", type.Name);
            foreach (var item in handlers)
                item.Handle(message).Wait();
        }

        private void VerifyInvocations(params string[] expected)
        {
            var expectedJoined = string.Join("\r\n", expected);
            var actualJoined = string.Join("\r\n", _invocations);
            Assert.AreEqual(expectedJoined, actualJoined, "Invocations");
        }

        private class TestMessage1 { public string Data; }
        private class TestMessage2 { public string Data; }
        private class TestMessage3 { public string Data; }

        private class TestHandler
            : IHandle<TestMessage1>
            , IHandle<TestMessage2>
            , IHandle<TestMessage3>
        {
            private List<string> _invocations;
            private string _name;

            public TestHandler(string name, List<string> invocations)
            {
                _name = name;
                _invocations = invocations;
            }

            public Task Handle(TestMessage1 message)
            {
                return HandleGeneric(message.GetType(), message.Data);
            }

            public Task Handle(TestMessage2 message)
            {
                return HandleGeneric(message.GetType(), message.Data);
            }

            public Task Handle(TestMessage3 message)
            {
                return HandleGeneric(message.GetType(), message.Data);
            }

            private Task HandleGeneric(Type type, string data)
            {
                _invocations.Add(string.Format("{0} {1}: {2}", _name, type.Name, data));
                return TaskResult.GetCompletedTask();
            }
        }
    }
}
