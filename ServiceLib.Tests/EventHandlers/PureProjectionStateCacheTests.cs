using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class PureProjectionStateCacheTests : PureProjectionTestBase
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private TestSerializer _serializer;
        private PureProjectionStateCache<TestState> _cache;
        private NotifyChangeDirect _notifier;
        private List<string> _notifications;

        [TestInitialize]
        public void Initialize()
        {
            _notifications = new List<string>();
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _serializer = new TestSerializer();
            _notifier = new NotifyChangeDirect(_executor);
            _cache = new PureProjectionStateCache<TestState>(_folder, _serializer);
            _cache.SetupFlushing(true, false, 3);
            _cache.SetupMruLimit(1);
            _cache.SetupNotificator(_notifier);
            _notifier.Register((s, v) => _notifications.Add(s));
        }

        [TestMethod]
        public void LoadExistingMetadata()
        {
            string version = null;
            EventStoreToken token = null;
            _folder.SaveDocument("__version", "2.4.20.1157");
            _folder.SaveDocument("__token", "55472");
            
            _cache.LoadMetadata((v, t) => { version = v; token = t; }, OnError);
            _executor.Process();
            
            Assert.AreEqual("2.4.20.1157", version, "Version");
            Assert.AreEqual(new EventStoreToken("55472"), token, "Token");
        }

        [TestMethod]
        public void LoadNonexistingMetadata()
        {
            string version = null;
            EventStoreToken token = null;
            
            _cache.LoadMetadata((v, t) => { version = v; token = t; }, OnError);
            _executor.Process();
            
            Assert.AreEqual("", version ?? "", "Version");
            Assert.AreEqual(EventStoreToken.Initial, token, "Token");
        }

        [TestMethod]
        public void SetOnlyTokenAndVersion()
        {
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            bool flushFinished = false;

            _cache.SetVersion("1.7.22");
            _cache.SetToken(new EventStoreToken("2745"));
            _cache.Flush(() => flushFinished = true, OnError);
            
            _executor.Process();
            Assert.IsTrue(flushFinished, "Finished");
            Assert.AreEqual("1.7.22", _folder.GetDocument("__version"), "Version");
            Assert.AreEqual("2745", _folder.GetDocument("__token"), "Token");
        }

        [TestMethod]
        public void GetNonexistentPartition()
        {
            TestState state = null;
            _cache.Get("A", s => { state = s; }, OnError);
            _executor.Process();
            Assert.AreEqual(TestState.Initial, state);
        }

        [TestMethod]
        public void GetExistingPartition()
        {
            TestState state = null;
            _folder.SaveDocument("B", "584\r\nE1:47");
            _cache.Get("B", s => { state = s; }, OnError);
            _executor.Process();
            Assert.AreEqual(new TestState("584", "E1:47"), state);
        }

        [TestMethod]
        public void SavePartitionAndFlush()
        {
            bool saveFinished = false;

            _cache.Set("C", new TestState("47", "E2:22"), 
                () => _cache.Flush(() => saveFinished = true, OnError), OnError);
            _executor.Process();

            Assert.IsTrue(saveFinished, "Finished");
            Assert.AreEqual("47\r\nE2:22", _folder.GetDocument("C"), "Contents");
            var notifications = string.Concat(_notifications.OrderBy(s => s));
            Assert.AreEqual("C", notifications, "Notifications");
        }

        [TestMethod]
        public void ResetAndFlush()
        {
            _folder.SaveDocument("__version", "0.5");
            _folder.SaveDocument("__token", "334");
            _folder.SaveDocument("A", "247\r\nOutputForA");
            _folder.SaveDocument("B", "334\r\nOutputForB");
            _folder.SaveDocument("C", "311\r\nOutputForC");
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            bool flushFinished = false;

            _cache.Reset("1.0", () => _cache.Flush(() => flushFinished = true, OnError), OnError);

            _executor.Process();
            Assert.IsTrue(flushFinished, "Finished");
            Assert.AreEqual("1.0", _folder.GetDocument("__version"), "Version");
            Assert.AreEqual("", _folder.GetDocument("__token"), "Token");
            Assert.AreEqual(null, _folder.GetDocument("A"), "A");
            Assert.AreEqual(null, _folder.GetDocument("B"), "B");
            Assert.AreEqual(null, _folder.GetDocument("C"), "C");
        }

        [TestMethod]
        public void RebuildMode()
        {
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            ProjectionReset("1.0");
            ProjectionCycle("A", "1", state => state.Add("E1:", "01").ApplyToken("1"));
            ProjectionCycle("B", "2", state => state.Add("E2:", "14").ApplyToken("2"));
            ProjectionCycle("C", "3", state => state.Add("E1:", "47").ApplyToken("3"));
            ProjectionCycle("B", "4", state => state.Add("E1:", "36").ApplyToken("4"));
            ProjectionCycle("A", "5", state => state.Add("E3:", "11").ApplyToken("5"));
            ProjectionCycle("A", "6", state => state.Add("E2:", "88").ApplyToken("6"));
            ProjectionCycle("A", "7", state => state.Add("E1:", "22").ApplyToken("7"));
            ProjectionFlush();
            Assert.AreEqual("7", _folder.GetDocument("__token"), "Token");
            Assert.AreEqual("1.0", _folder.GetDocument("__version"), "Version");
            Assert.AreEqual("7\r\nE1:01 E3:11 E2:88 E1:22", _folder.GetDocument("A"), "A");
            Assert.AreEqual("4\r\nE2:14 E1:36", _folder.GetDocument("B"), "B");
            Assert.AreEqual("3\r\nE1:47", _folder.GetDocument("C"), "C");
            Assert.AreEqual(1, _folder.GetVersion("__token"), "Token");
            Assert.AreEqual(1, _folder.GetVersion("__version"), "Version");
            Assert.AreEqual(1, _folder.GetVersion("A"), "A");
            Assert.AreEqual(1, _folder.GetVersion("B"), "B");
            Assert.AreEqual(1, _folder.GetVersion("C"), "C");
        }

        [TestMethod]
        public void NormalModeContents()
        {
            _folder.SaveDocument("__version", "1.0");
            _folder.SaveDocument("__token", "");
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            ProjectionCycle("A", "1", state => state.Add("E1:", "01").ApplyToken("1"));
            ProjectionCycle("B", "2", state => state.Add("E2:", "14").ApplyToken("2"));
            ProjectionCycle("C", "3", state => state.Add("E1:", "47").ApplyToken("3"));
            ProjectionCycle("B", "4", state => state.Add("E1:", "36").ApplyToken("4"));
            ProjectionCycle("A", "5", state => state.Add("E3:", "11").ApplyToken("5"));
            ProjectionCycle("A", "6", state => state.Add("E2:", "88").ApplyToken("6"));
            ProjectionCycle("A", "7", state => state.Add("E1:", "22").ApplyToken("7"));
            ProjectionFlush();
            Assert.AreEqual("7", _folder.GetDocument("__token"), "Token");
            Assert.AreEqual("1.0", _folder.GetDocument("__version"), "Version");
            Assert.AreEqual("7\r\nE1:01 E3:11 E2:88 E1:22", _folder.GetDocument("A"), "A");
            Assert.AreEqual("4\r\nE2:14 E1:36", _folder.GetDocument("B"), "B");
            Assert.AreEqual("3\r\nE1:47", _folder.GetDocument("C"), "C");
        }

        [TestMethod]
        public void NormalModeFlushing()
        {
            _folder.SaveDocument("__version", "1.0", 1);
            _folder.SaveDocument("__token", "", 1);
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            ProjectionCycle("A", "1", state => state.Add("E1:", "01").ApplyToken("1"));
            ProjectionCycle("B", "2", state => state.Add("E2:", "14").ApplyToken("2"));
            ProjectionCycle("C", "3", state => state.Add("E1:", "47").ApplyToken("3"));
            ProjectionCycle("B", "4", state => state.Add("E1:", "36").ApplyToken("4"));
            ProjectionCycle("A", "5", state => state.Add("E3:", "11").ApplyToken("5"));
            ProjectionCycle("A", "6", state => state.Add("E2:", "88").ApplyToken("6"));
            ProjectionCycle("A", "7", state => state.Add("E1:", "22").ApplyToken("7"));
            ProjectionFlush();
            Assert.AreEqual(4, _folder.GetVersion("__token"), "Token");
            Assert.AreEqual(1, _folder.GetVersion("__version"), "Version");
            Assert.AreEqual(3, _folder.GetVersion("A"), "A");
            Assert.AreEqual(2, _folder.GetVersion("B"), "B");
            Assert.AreEqual(1, _folder.GetVersion("C"), "C");
            var notifications = string.Concat(_notifications.OrderBy(s => s));
            Assert.AreEqual("AAABBC", notifications, "Notifications");
        }

        [TestMethod]
        public void EjectingUnusedPartitions()
        {
            _folder.SaveDocument("__version", "1.0", 1);
            _folder.SaveDocument("__token", "", 1);
            _folder.SaveDocument("A", "1\r\nContents", 1);
            _folder.SaveDocument("B", "1\r\nContents", 1);
            _cache.LoadMetadata((v, t) => { }, OnError);
            _executor.Process();
            _folder.ClearLog();
            ProjectionGet("A");
            ProjectionGet("B");
            ProjectionFlush();  // LL
            ProjectionGet("B");
            ProjectionFlush();  // 0T
            ProjectionGet("A");
            ProjectionGet("B");
            ProjectionFlush();  // TT
            ProjectionFlush();  // 00
            ProjectionFlush();  // EE
            ProjectionGet("A");
            ProjectionGet("B");
            ProjectionFlush();  // LL
            var filteredLog = _folder.LogLines().Where(s => s.StartsWith("Get ")).ToList();
            var loadedNames = string.Concat(filteredLog.Select(s => s.Substring(4, 1)));
            Assert.AreEqual("ABAB", loadedNames);
        }

        private void ProjectionReset(string version)
        {
            bool resetFinished = false;
            _cache.Reset(version, () => { resetFinished = true; }, OnError);
            _executor.Process();
            Assert.IsTrue(resetFinished, "Reset() should have finished");
        }

        private TestState ProjectionGet(string partition)
        {
            TestState state = null;
            _cache.Get(partition, s => state = s, OnError);
            _executor.Process();
            Assert.IsNotNull(state, "State null");
            return state;
        }

        private void ProjectionCycle(string partition, string token, Func<TestState, TestState> change)
        {
            string phase = "started";
            _cache.Get(partition, 
                state => {
                    phase = "get";
                    _cache.Set(partition, change(state), 
                        () => 
                        {
                            phase = "set";
                            _cache.SetToken(new EventStoreToken(token));
                            phase = "settoken";
                        }, OnError);
                }, 
                OnError);
            _executor.Process();
            Assert.AreEqual("settoken", phase, "Phase");
        }

        private void ProjectionFlush()
        {
            bool flushFinished = false;
            _cache.Flush(() => { flushFinished = true; }, OnError);
            _executor.Process();
            Assert.IsTrue(flushFinished, "Flush() should have finished");
        }

        private void OnError(Exception ex)
        {
            throw ex.PreserveStackTrace();
        }
    }
}
