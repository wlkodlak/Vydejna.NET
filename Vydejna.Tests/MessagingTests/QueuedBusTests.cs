using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;
using Moq;
using System.Threading;

namespace Vydejna.Tests.MessagingTests
{
    [TestClass]
    public class QueuedBusTests
    {
        private ISubscriptionManager _subscriptions;
        private QueuedBus _bus;
        private List<string> _invocations;
        private TestHandler _handler;

        [TestInitialize]
        public void Initialize()
        {
            _subscriptions = new SubscriptionManager();
            _bus = new QueuedBus(_subscriptions);
            _invocations = new List<string>();
            _handler = new TestHandler(_invocations);
        }

        [TestMethod]
        public void RegistrationDelegated()
        {
            _bus.Subscribe<TestMessage>(_handler);
            var expected = "TestMessage";
            var actual = string.Join(", ", _subscriptions.GetHandledTypes().Select(x => x.Name));
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void HandleDelayed()
        {
            _bus.Subscribe<TestMessage>(_handler);
            _bus.Publish(new TestMessage { Data = "Hello" });
            Assert.AreEqual(0, _invocations.Count, "Invocation before handling");
            _bus.HandleNext();
            var expectedJoined = "TestMessage: Hello";
            var actualJoined = string.Join("\r\n", _invocations);
            Assert.AreEqual(expectedJoined, actualJoined, "Found invocations");
        }

        [TestMethod]
        public void HandleReturnsTrueWhenNonEmpty()
        {
            _bus.Subscribe<TestMessage>(_handler);
            _bus.Publish(new TestMessage { Data = "Hello" });
            Assert.AreEqual(0, _invocations.Count, "Invocation before handling");
            var processed = _bus.HandleNext();
            Assert.IsTrue(processed, "Anything processed");
        }

        [TestMethod]
        public void HandleErrors()
        {
            var wasCalled = false;
            var storedException = (Exception)null;
            var storedMessage = (TestMessage)null;
            var exception = new InvalidOperationException();
            var message = new TestMessage { Data = "Hello" };
            var reg = _bus.Subscribe<TestMessage>(msg => TaskResult.GetFailedTask(exception));
            reg.HandleErrorsWith((m, e) =>
            {
                wasCalled = true;
                storedMessage = m;
                storedException = e;
            });
            _bus.Publish(message);
            _bus.HandleNext();
            Assert.IsTrue(wasCalled, "Was called");
            Assert.AreSame(message, storedMessage, "Message");
            Assert.AreSame(exception, storedException, "Exception");
        }

        [TestMethod]
        public void NothingToProcess()
        {
            Assert.IsFalse(_bus.HandleNext());
        }

        [TestMethod]
        public void WaitForMessages_MessageArrived()
        {
            _bus.Subscribe<TestMessage>(m => { });
            var cancel = new CancellationTokenSource();
            var taskWait = Task.Factory.StartNew(() => _bus.WaitForMessages(cancel.Token));
            Assert.IsFalse(taskWait.Wait(1), "Should not be completed yet");
            _bus.Publish(new TestMessage());
            Assert.IsTrue(taskWait.Wait(200), "WaitForMessages didn't finish in time");

        }

        [TestMethod]
        public void WaitForMessages_Cancelled()
        {
            _bus.Subscribe<TestMessage>(m => { });
            var cancel = new CancellationTokenSource();
            var taskWait = Task.Factory.StartNew(() => _bus.WaitForMessages(cancel.Token));
            Assert.IsFalse(taskWait.Wait(1), "Should not be completed yet");
            cancel.Cancel();
            Assert.IsTrue(taskWait.Wait(200), "WaitForMessages didn't finish in time");

        }

        protected class TestMessage { public string Data; }

        protected class TestHandler : IHandle<TestMessage>
        {
            private List<string> _invocations;

            public TestHandler(List<string> invocations)
            {
                _invocations = invocations;
            }

            public Task Handle(TestMessage message)
            {
                _invocations.Add(string.Format("{0}: {1}", message.GetType().Name, message.Data));
                return TaskResult.GetCompletedTask();
            }
        }
    }
}
