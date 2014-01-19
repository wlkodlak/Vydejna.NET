using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.EventHandlers
{
    [TestClass]
    public class PureProjectionReaderTests : PureProjectionTestBase
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private TestSerializer _serializer;
        private PureProjectionReader<TestState> _reader;
        private VirtualTime _time;
        private NotifyChangeDirect _notify;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _serializer = new TestSerializer();
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2013, 10, 11, 18, 22, 22));
            _notify = new NotifyChangeDirect(_executor);
            _reader = new PureProjectionReader<TestState>(_folder, _serializer, _executor, _time, _notify);
        }

        [TestMethod]
        public void RetrievingUnknownPartitionCausesLoading()
        {
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);

            _reader.Get("doc", s => { }, ThrowError);

            _executor.Process();
            CollectionAssert.Contains(_folder.LogLines(), "Get doc@1", "Loaded");
        }

        [TestMethod]
        public void IfPartitionIsBeingLoadedDoNotStartAnotherLoadingOfIt()
        {
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);
            _reader.Get("doc", s => { }, ThrowError);

            _reader.Get("doc", s => { }, ThrowError);

            _executor.Process();
            Assert.AreEqual(1, _folder.LogLines().Count(l => l == "Get doc@1"), "Loads");
        }

        [TestMethod]
        public void WhenPartitionGetsLoadedAllWaitersShouldReceiveState()
        {
            TestState loadedState1 = null;
            TestState loadedState2 = null;
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);

            _reader.Get("doc", s => loadedState1 = s, ThrowError);
            _reader.Get("doc", s => loadedState2 = s, ThrowError);
            _executor.Process();

            Assert.IsNotNull(loadedState1, "State 1 retrieved");
            Assert.IsNotNull(loadedState1, "State 2 retrieved");
            Assert.AreEqual(new EventStoreToken("875"), loadedState1.Token, "Retrieved token 1");
            Assert.AreEqual(new EventStoreToken("875"), loadedState2.Token, "Retrieved token 2");
        }

        [TestMethod]
        public void PartitionLoadedRecentlyShouldNotBeLoadedAgain()
        {
            TestState loadedState = null;
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);
            _reader.Get("doc", s => { }, ThrowError);
            _executor.Process();
            _reader.EndCycle();
            _executor.Process();
            _folder.ClearLog();

            _reader.Get("doc", s => loadedState = s, ThrowError);

            _executor.Process();
            Assert.IsNotNull(loadedState, "State retrieved");
            Assert.AreEqual(new EventStoreToken("875"), loadedState.Token, "Retrieved token");
            Assert.AreEqual(0, _folder.LogLines().Count, "Log count");
        }

        [TestMethod]
        public void LongUnusedPartitionShouldBeLoadedAgain()
        {
            TestState loadedState = null;
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);
            _reader.Get("doc", s => { }, ThrowError);
            _executor.Process();
            for (int i = 0; i < 20; i++)
                _reader.EndCycle();
            _folder.ClearLog();

            _reader.Get("doc", s => loadedState = s, ThrowError);

            _executor.Process();
            Assert.IsNotNull(loadedState, "State retrieved");
            Assert.AreEqual(new EventStoreToken("875"), loadedState.Token, "Retrieved token");
            CollectionAssert.Contains(_folder.LogLines(), "Get doc@1", "Loaded");
        }

        [TestMethod]
        public void WhenPartitionIsChangedNextRetrievalShouldLoadIt()
        {
            TestState loadedState = null;
            _folder.SaveDocument("doc", "875\r\nE1: 44", 1);
            _reader.Get("doc", s => { }, ThrowError);
            _executor.Process();
            _folder.SaveDocument("doc", "999\r\nE2: 87", 2);
            _time.SetTime(new DateTime(2013, 10, 11, 18, 22, 24));
            _executor.Process();
            _folder.ClearLog();

            _reader.Get("doc", s => loadedState = s, ThrowError);

            _executor.Process();
            Assert.IsNotNull(loadedState, "State retrieved");
            Assert.AreEqual(new EventStoreToken("999"), loadedState.Token, "Retrieved token");
            Assert.AreEqual("E2: 87", loadedState.Output, "Retrieved data");
            CollectionAssert.Contains(_folder.LogLines(), "Get doc@2", "Loaded");
        }
    }
}
