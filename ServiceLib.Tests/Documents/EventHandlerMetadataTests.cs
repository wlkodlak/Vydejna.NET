using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class EventHandlerMetadataTests
    {
        private TestScheduler _scheduler;
        private TestDocumentFolder _folder;
        private MetadataManager _mgr;
        private IMetadataInstance _inst;
        
        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _folder = new TestDocumentFolder();
            _mgr = new MetadataManager(_folder);
            _inst = _mgr.GetConsumer("consumer");
        }

        [TestMethod]
        public void GetNonexistingToken()
        {
            var taskToken = _scheduler.Run(() => _inst.GetToken());
            Assert.IsTrue(taskToken.IsCompleted, "Complete");
            var token = taskToken.Result;
            Assert.AreEqual(EventStoreToken.Initial, token);
        }

        [TestMethod]
        public void GetExistingToken()
        {
            _folder.SaveDocumentSync("consumer_tok", "5584");
            var taskToken = _scheduler.Run(() => _inst.GetToken());
            Assert.IsTrue(taskToken.IsCompleted, "Complete");
            var token = taskToken.Result;
            Assert.AreEqual(new EventStoreToken("5584"), token);
        }

        [TestMethod]
        public void GetNonexistingVersion()
        {
            var taskVersion = _scheduler.Run(() => _inst.GetVersion());
            Assert.IsTrue(taskVersion.IsCompleted, "Complete");
            var version = taskVersion.Result;
            Assert.AreEqual("", version ?? "");
        }

        [TestMethod]
        public void GetExistingVersion()
        {
            _folder.SaveDocumentSync("consumer_ver", "1.12");
            var taskVersion = _scheduler.Run(() => _inst.GetVersion());
            Assert.IsTrue(taskVersion.IsCompleted, "Complete");
            var version = taskVersion.Result;
            Assert.AreEqual("1.12", version);
        }
    }
}
