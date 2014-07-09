using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class EventTrackingTests
    {
        private VirtualTime _time;
        private EventProcessTracking _coordinator;
        private IEventProcessTrackSource _tracker;
        private List<IEventProcessTrackTarget> _targets;
        private TestScheduler _scheduler;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2014, 6, 18, 22, 15, 33));
            _scheduler = new TestScheduler();
            _targets = new List<IEventProcessTrackTarget>();
            _coordinator = new EventProcessTracking(_time);
            _coordinator.Init(s => { }, _scheduler);
            _coordinator.Start();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_coordinator.State != ProcessState.Inactive)
            {
                _coordinator.Stop();
                _scheduler.Process();
            }
        }

        private IEventProcessTrackItem CompleteTracker(params string[] tokens)
        {
            var source = _coordinator.CreateTracker();
            var trackingId = source.TrackingId;
            Assert.IsNotNull(trackingId, "TrackSource.TrackingId");
            foreach (var tokenString in tokens)
                source.AddEvent(new EventStoreToken(tokenString));
            source.CommitToTracker();
            var foundTracker = _coordinator.FindTracker(trackingId);
            Assert.IsNotNull(foundTracker, "TrackItem");
            Assert.AreEqual(trackingId, foundTracker.TrackingId, "TrackItem.TrackingId");
            return foundTracker;
        }

        private void CreateHandlers(params string[] names)
        {
            foreach (var name in names)
                _targets.Add(_coordinator.RegisterHandler(name));
        }

        private void ReportProgress(string handlerName, string finishedToken)
        {
            var handler = _coordinator.RegisterHandler(handlerName);
            var token = new EventStoreToken(finishedToken);
            handler.ReportProgress(token);
            _scheduler.Process();
        }

        private Task<bool> StartWaiting(IEventProcessTrackItem tracker, int timeout)
        {
            return _scheduler.Run<bool>(() => tracker.WaitForFinish(timeout), timeout == 0);
        }

        private void AdvanceTime(int milliseconds)
        {
            _time.SetTime(_time.GetUtcTime().AddMilliseconds(milliseconds));
            _scheduler.Process();
        }

        private void AssertCompleted(Task<bool> waitTask, bool expectedCompletion)
        {
            _scheduler.Process();
            Assert.IsTrue(waitTask.IsCompleted, "Wait finished");
            Assert.AreEqual(expectedCompletion, waitTask.Result, "Event handling completed");
        }

        [TestMethod]
        public void CompletedNowait()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            ReportProgress("Handler1", "18");
            var task = StartWaiting(tracker, 0);
            AssertCompleted(task, true);
        }

        [TestMethod]
        public void UnfinishedNowait()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 0);
            AssertCompleted(task, false);
        }

        [TestMethod]
        public void CompletedAfterShortWait()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 100);
            ReportProgress("Handler1", "18");
            AssertCompleted(task, true);
        }

        [TestMethod]
        public void CompletedWhenLargerTokenIsProcessed()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 100);
            ReportProgress("Handler1", "20");
            AssertCompleted(task, true);
        }

        [TestMethod]
        public void NotCompletedWhenOnlySmallersTokenAreProcessed()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 100);
            ReportProgress("Handler1", "5");
            ReportProgress("Handler1", "10");
            Assert.IsFalse(task.IsCompleted, "Should not be complete yet");
        }

        [TestMethod]
        public void PartiallyComplete()
        {
            CreateHandlers("Handler1", "Handler2");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 100);
            ReportProgress("Handler1", "20");
            ReportProgress("Handler2", "10");
            Assert.IsFalse(task.IsCompleted, "Should not be complete yet");
        }

        [TestMethod]
        public void FullyComplete()
        {
            CreateHandlers("Handler1", "Handler2");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 100);
            ReportProgress("Handler1", "20");
            ReportProgress("Handler2", "18");
            AssertCompleted(task, true);
        }

        [TestMethod]
        public void UncompleteWaitAfterTimeout()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 500);
            AdvanceTime(1000);
            AssertCompleted(task, false);
        }

        [TestMethod]
        public void UncompleteWaitAfterSystemStop()
        {
            CreateHandlers("Handler1");
            var tracker = CompleteTracker("18");
            var task = StartWaiting(tracker, 500);
            _coordinator.Stop();
            AssertCompleted(task, false);
        }
    }
}
