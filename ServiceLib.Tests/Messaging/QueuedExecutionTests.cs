using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System.Linq;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class QueuedExecutionTests
    {
        [TestMethod]
        public void EnqueueInterfacePublishesToBus()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            var queue = new QueuedExecutionWorker(bus);
            queue.Subscribe(bus);
            bool hasExecuted = false;
            queue.Enqueue(() => hasExecuted = true);
            Assert.IsFalse(hasExecuted, "Should not execute immediatelly");
            while (bus.HandleNext()) ;
            Assert.IsTrue(hasExecuted, "Should execute using bus");
        }
    }
    [TestClass]
    public class QueuedCommandExecutionTests
    {
        private CommandSubscriptionManager _testSubscriptions;
        private TestExecutor _testExecutor;
        private QueuedCommandSubscriptionManager _subscriptions;
        private TestHandler _testHandler;
        
        [TestInitialize]
        public void Initialize()
        {
            _testSubscriptions = new CommandSubscriptionManager();
            _testExecutor = new TestExecutor();
            _testHandler = new TestHandler();
            _subscriptions = new QueuedCommandSubscriptionManager(_testSubscriptions, _testExecutor);
        }

        [TestMethod]
        public void GetHandledTypesRedirects()
        {
            _subscriptions.Register<TestMessage>(_testHandler);
            var handledTypes = _subscriptions.GetHandledTypes();
            Assert.IsNotNull(handledTypes, "Handled types null");
            Assert.AreEqual("TestMessage", string.Join(", ", handledTypes.Select(t => t.Name)));
        }

        [TestMethod]
        public void ExecutionIsDelayed()
        {
            _subscriptions.Register<TestMessage>(_testHandler);
            var handler = _subscriptions.FindHandler(typeof(TestMessage));
            bool completed = false;
            handler.Handle(new TestMessage(), () => completed = true, ex => { });
            Assert.IsFalse(completed, "Completed before it should");
            _testExecutor.Process();
            Assert.IsTrue(completed, "Completed when it should");

        }

        private class TestMessage { }

        private class TestHandler : IHandle<CommandExecution<TestMessage>>
        {
            public void Handle(CommandExecution<TestMessage> message)
            {
                message.OnCompleted();
            }
        }
    }
}
