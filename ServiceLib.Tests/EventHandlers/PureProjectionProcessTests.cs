using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class PureProjectionProcessTests : PureProjectionTestBase
    {
        private TestExecutor _executor;
        private TestHandler _handler;
        private TestNodeLock _locking;
        private TestCache _cache;
        private TestStreaming _streaming;
        private IPureProjectionDispatcher<TestState> _dispatcher;
        private PureProjectionProcess<TestState> _process;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _handler = new TestHandler();
            _locking = new TestNodeLock();
            _cache = new TestCache();
            _dispatcher = new PureProjectionDispatcherDeduplication<TestState>(
                new PureProjectionDispatcher<TestState>(), _handler);
            _streaming = new TestStreaming(_executor);
            _dispatcher.Register<TestEvent1>(_handler);
            _dispatcher.Register<TestEvent2>(_handler);
            _dispatcher.Register<TestEvent3>(_handler);
            _process = new PureProjectionProcess<TestState>(_handler, _locking, _cache, _dispatcher, _streaming);
        }

        [TestMethod]
        public void AttemptToLockedCancelledOnShutdown()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _process.Handle(new SystemEvents.SystemShutdown());
            Assert.IsFalse(_locking.IsWaiting);
        }

        [TestMethod]
        public void RebuildWhenVersionEmpty()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _executor.Process();
            Assert.IsTrue(_cache.WasReset, "WasReset");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken, "Reading token");
            Assert.AreEqual("TestEvent1, TestEvent2, TestEvent3", string.Join(", ", _streaming.SupportedTypes()), "Types");
        }

        [TestMethod]
        public void RebuildWhenVersionDifferent()
        {
            _cache.Version = "0.8";
            _cache.Token = new EventStoreToken("447");
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _executor.Process();
            Assert.IsTrue(_cache.WasReset, "WasReset");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(EventStoreToken.Initial, _streaming.CurrentToken, "Reading token");
            Assert.AreEqual("TestEvent1, TestEvent2, TestEvent3", string.Join(", ", _streaming.SupportedTypes()), "Types");
        }

        [TestMethod]
        public void NoRebuildWhenVersionSame()
        {
            _cache.Version = "1.0";
            _cache.Token = new EventStoreToken("447");
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _executor.Process();
            Assert.IsFalse(_cache.WasReset, "WasReset");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(new EventStoreToken("447"), _streaming.CurrentToken, "Reading token");
            Assert.AreEqual("TestEvent1, TestEvent2, TestEvent3", string.Join(", ", _streaming.SupportedTypes()), "Types");
        }

        [TestMethod]
        public void ProcessSingleEventInRebuild()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _streaming.AddEvent("1", new TestEvent1() { Partition = "A", Data = "14" });
            _executor.Process();
            Assert.AreEqual(new EventStoreToken("1"), _streaming.CurrentToken, "Streaming token");
            Assert.IsTrue(_streaming.IsReading, "IsReading");
            Assert.AreEqual(new TestState("1", "E1:14"), _cache.Get("A"), "A");
            Assert.AreEqual(new EventStoreToken("1"), _cache.Token, "Cache token");
        }

        [TestMethod]
        public void CompleteInitialBuild()
        {
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _executor.Process();
            _streaming.AddEvent("1", new TestEvent1() { Partition = "A", Data = "14" });
            _streaming.AddEvent("2", new TestEvent3() { Partition = "B", Data = "88" });
            _streaming.AddEvent("3", new TestEvent2() { Partition = "C", Data = "37" });
            _streaming.AddEvent("5", new TestEvent2() { Partition = "B", Data = "47" });
            _streaming.AddEvent("6", new TestEvent1() { Partition = "B", Data = "48" });
            _streaming.AddEvent("7", new TestEvent3() { Partition = "A", Data = "21" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.AddEvent("8", new TestEvent2() { Partition = "A", Data = "39" });
            _streaming.AddEvent("9", new TestEvent1() { Partition = "C", Data = "92" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _process.Handle(new SystemEvents.SystemShutdown());
            _executor.Process();
            Assert.AreEqual(new EventStoreToken("9"), _cache.Token, "Token");
            Assert.AreEqual("1.0", _cache.Version, "Version");
            Assert.IsFalse(_cache.IsDirty, "IsDirty");
            Assert.AreEqual(new TestState("8", "E1:14 E3:21 E2:39"), _cache.Get("A"), "A");
            Assert.AreEqual(new TestState("6", "E3:88 E2:47 E1:48"), _cache.Get("B"), "B");
            Assert.AreEqual(new TestState("9", "E2:37 E1:92"), _cache.Get("C"), "C");
        }

        [TestMethod]
        public void CompleteNormalFunction()
        {
            _cache.Token = new EventStoreToken("7");
            _cache.Version = "1.0";
            _cache.Set("A", new TestState("7", "E1:14 E3:21"));
            _cache.Set("B", new TestState("6", "E3:88 E2:47 E1:48"));
            _cache.Set("C", new TestState("3", "E2:37"));
            _process.Handle(new SystemEvents.SystemInit());
            _locking.SendLock();
            _executor.Process();
            _streaming.AddEvent("7", new TestEvent3() { Partition = "A", Data = "21" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _streaming.AddEvent("8", new TestEvent2() { Partition = "A", Data = "39" });
            _streaming.AddEvent("9", new TestEvent1() { Partition = "C", Data = "92" });
            _streaming.MarkEndOfStream();
            _executor.Process();
            _process.Handle(new SystemEvents.SystemShutdown());
            _executor.Process();
            Assert.AreEqual(new EventStoreToken("9"), _cache.Token, "Token");
            Assert.AreEqual("1.0", _cache.Version, "Version");
            Assert.IsFalse(_cache.IsDirty, "IsDirty");
            Assert.AreEqual(new TestState("8", "E1:14 E3:21 E2:39"), _cache.Get("A"), "A");
            Assert.AreEqual(new TestState("6", "E3:88 E2:47 E1:48"), _cache.Get("B"), "B");
            Assert.AreEqual(new TestState("9", "E2:37 E1:92"), _cache.Get("C"), "C");
        }

        private class TestCache : IPureProjectionStateCache<TestState>
        {
            public bool IsFailing = false;
            private Dictionary<string, TestState> _data = new Dictionary<string, TestState>();
            public string Version = "";
            public EventStoreToken Token = EventStoreToken.Initial;
            public bool FlushEnabled = true;
            public bool FlushRebuild = false;
            public int FlushCounter = 20;
            public bool WasReset = false;
            public bool IsDirty = false;

            private bool MaybeError(Action<Exception> onError)
            {
                if (!IsFailing)
                    return true;
                else
                {
                    onError(new Exception("Simulated failure"));
                    return false;
                }
            }

            public void Get(string partition, Action<TestState> onCompleted, Action<Exception> onError)
            {
                if (MaybeError(onError))
                {
                    TestState state;
                    if (!_data.TryGetValue(partition, out state))
                        _data[partition] = state = TestState.Initial;
                    onCompleted(state);
                }
            }

            public TestState Get(string partition)
            {
                TestState state;
                _data.TryGetValue(partition, out state);
                return state;
            }

            public void Set(string partition, TestState state, Action onCompleted, Action<Exception> onError)
            {
                if (MaybeError(onError))
                {
                    _data[partition] = state;
                    onCompleted();
                }
            }

            public void Set(string partition, TestState state)
            {
                _data[partition] = state;
                IsDirty = true;
            }

            public void Reset(string version, Action onCompleted, Action<Exception> onError)
            {
                if (MaybeError(onError))
                {
                    IsDirty = true;
                    Version = version;
                    Token = EventStoreToken.Initial;
                    _data.Clear();
                    WasReset = true;
                    onCompleted();
                }
            }

            public void Flush(Action onCompleted, Action<Exception> onError)
            {
                if (MaybeError(onError))
                {
                    IsDirty = false;
                    onCompleted();
                }
            }

            public void LoadMetadata(Action<string, EventStoreToken> onCompleted, Action<Exception> onError)
            {
                if (MaybeError(onError))
                    onCompleted(Version, Token);
            }

            public void SetVersion(string version)
            {
                Version = version;
                IsDirty = true;
            }

            public void SetToken(EventStoreToken token)
            {
                Token = token;
                IsDirty = true;
            }

            public void SetupFlushing(bool enabled, bool allowWhenRebuilding, int counter)
            {
                FlushEnabled = enabled;
                FlushRebuild = allowWhenRebuilding;
                FlushCounter = counter;
            }
        }
    }
}
