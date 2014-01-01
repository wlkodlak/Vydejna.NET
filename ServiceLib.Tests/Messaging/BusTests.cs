using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using System.Threading;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class DirectBusTests
    {
        [TestMethod]
        public void DirectBusExecutesHandlersWhenPublished()
        {
            var bus = new DirectBus(new SubscriptionManager(), "bus");
            var sb = new StringBuilder();
            bus.Subscribe<TestMessage1>(msg => sb.AppendFormat("Msg1: {0}\r\n", msg.Data));
            bus.Subscribe<TestMessage2>(msg => sb.AppendFormat("Msg2: {0}\r\n", msg.Data));

            bus.Publish(new TestMessage1 { Data = "Hello" });
            bus.Publish(new TestMessage2 { Data = "World" });
            bus.Publish(new TestMessage1 { Data = "!!!" });

            var expected = "Msg1: Hello\r\nMsg2: World\r\nMsg1: !!!\r\n";
            Assert.AreEqual(expected, sb.ToString());
        }

        [TestMethod]
        public void QueuedBusDoesNotExecuteHandlersWhenPublished()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            var sb = new StringBuilder();
            bus.Subscribe<TestMessage1>(msg => sb.AppendFormat("Msg1: {0}\r\n", msg.Data));
            bus.Subscribe<TestMessage2>(msg => sb.AppendFormat("Msg2: {0}\r\n", msg.Data));

            bus.Publish(new TestMessage1 { Data = "Hello" });
            bus.Publish(new TestMessage2 { Data = "World" });
            bus.Publish(new TestMessage1 { Data = "!!!" });

            Assert.AreEqual("", sb.ToString());
        }

        [TestMethod]
        public void QueuedBusExecutesHandlersOnHandleNext()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            var sb = new StringBuilder();
            bus.Subscribe<TestMessage1>(msg => sb.AppendFormat("Msg1: {0}\r\n", msg.Data));
            bus.Subscribe<TestMessage2>(msg => sb.AppendFormat("Msg2: {0}\r\n", msg.Data));
            bus.Publish(new TestMessage1 { Data = "Hello" });
            bus.Publish(new TestMessage2 { Data = "World" });
            bus.Publish(new TestMessage1 { Data = "!!!" });

            while (bus.HandleNext()) ;

            var expected = "Msg1: Hello\r\nMsg2: World\r\nMsg1: !!!\r\n";
            Assert.AreEqual(expected, sb.ToString());
        }

        [TestMethod]
        public void QueuedBusDoesNotCrashOnErrors()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            bus.Subscribe<TestMessage1>(msg => { throw new Exception(); });
            bus.Publish(new TestMessage1());
            while (bus.HandleNext()) ;
        }

        [TestMethod, Timeout(100)]
        public void QueuedBusDoesNotWaitWhenMessagesAreAvailable()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            bus.Subscribe<TestMessage1>(msg => { });
            bus.Publish(new TestMessage1());
            bus.WaitForMessages(CancellationToken.None);
        }

        [TestMethod]
        public void QueuedBusWaitsUntilCancellation()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            using (var cancel = new CancellationTokenSource())
            {
                var mre = new ManualResetEventSlim();
                bus.Subscribe<TestMessage1>(msg => { });
                Task.Factory.StartNew(() => { mre.Set(); bus.WaitForMessages(cancel.Token); mre.Set(); });
                mre.Wait(100);
                mre.Reset();
                cancel.Cancel();
                Assert.IsTrue(mre.Wait(100), "Should have been cancelled");
            }
        }

        [TestMethod]
        public void QueuedBusWaitsUntilMessgeArrives()
        {
            var bus = new QueuedBus(new SubscriptionManager(), "bus");
            var mre = new ManualResetEventSlim();
            bus.Subscribe<TestMessage1>(msg => { });
            Task.Factory.StartNew(() => { mre.Set(); bus.WaitForMessages(CancellationToken.None); mre.Set(); });
            mre.Wait(100);
            mre.Reset();
            bus.Publish(new TestMessage1());
            Assert.IsTrue(mre.Wait(100), "Should not wait any more");
        }

        private class TestMessage1
        {
            public string Data;
        }
        private class TestMessage2
        {
            public string Data;
        }
    }
}
