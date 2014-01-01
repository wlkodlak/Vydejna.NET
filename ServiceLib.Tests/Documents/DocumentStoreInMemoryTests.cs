using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class DocumentStoreInMemoryTests
    {
        private IDocumentStore _store;
        private TestExecutor _executor;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _store = CreateEmptyDocumentStore(_executor);
        }

        [TestMethod]
        public void GettingNonexistentDocument()
        {
            VerifyDocument("testing", "document", DocumentStoreVersion.New, null);
        }

        [TestMethod]
        public void CreatingNewDocument()
        {
            SaveDocument("testing", "document", DocumentStoreVersion.New, "new contents", "saved");
            VerifyDocument("testing", "document", DocumentStoreVersion.At(1), "new contents");
        }

        [TestMethod]
        public void ConflictWhenSavingNew()
        {
            SetupDocument("testing", "document", "original");
            SaveDocument("testing", "document", DocumentStoreVersion.New, "new contents", "conflict");
            VerifyDocument("testing", "document", DocumentStoreVersion.At(1), "original");
        }

        [TestMethod]
        public void DeleteAll()
        {
            SetupDocument("testing", "document", "original");
            DeleteAll("testing");
            VerifyDocument("testing", "document", DocumentStoreVersion.New, null);
        }

        [TestMethod]
        public void WatchChanges_SameDocument()
        {
            SetupDocument("testing", "document", "original");
            int changes = 0;
            _store.SubFolder("testing").WatchChanges("document", () => changes++);
            SaveDocument("testing", "document", DocumentStoreVersion.Any, "new contents", "saved");
            Assert.AreEqual(1, changes, "Changes count");
        }

        [TestMethod]
        public void WatchChanges_DifferentDocument()
        {
            SetupDocument("testing", "document1", "A");
            SetupDocument("testing", "document2", "B");
            int changes = 0;
            _store.SubFolder("testing").WatchChanges("document1", () => changes++);
            SaveDocument("testing", "document2", DocumentStoreVersion.Any, "new contents", "saved");
            Assert.AreEqual(0, changes, "Changes count");
        }

        protected void SetupDocument(string folder, string name, string contents)
        {
            _store.SubFolder(folder).SaveDocument(name, contents, DocumentStoreVersion.Any,
                () => { }, () => { }, ex => { });
            _executor.Process();
        }

        protected void VerifyDocument(string folder, string name, DocumentStoreVersion expectedVersion, string expectedContents)
        {
            string contents = "";
            int version = 0;
            bool failed = false;
            _store.SubFolder(folder).GetDocument(name, (v, c) => { version = v; contents = c; }, () => { }, ex => failed = true);
            _executor.Process();
            Assert.IsFalse(failed, "GetDocument({0}) failed", name);
            if (!expectedVersion.VerifyVersion(version))
                Assert.AreEqual(expectedVersion, DocumentStoreVersion.At(version), "Unexpected version for {0}", name);
            Assert.AreEqual(expectedContents ?? "", contents, "Contents for {0}", name);
        }

        protected void DeleteAll(string folder)
        {
            _store.SubFolder(folder).DeleteAll(() => { }, ex => { });
            _executor.Process();
        }

        protected void SaveDocument(string folder, string name, DocumentStoreVersion expectedVersion, string contents, string expectedOutcome)
        {
            string resultSave = null;
            _store.SubFolder(folder).SaveDocument(name, contents, expectedVersion, 
                () => resultSave = "saved", () => resultSave = "conflict", ex => resultSave = "error");
            _executor.Process();
            Assert.AreEqual(expectedOutcome, resultSave, "Outcome for {0}", name);
        }


        protected virtual IDocumentStore CreateEmptyDocumentStore(IQueueExecution executor)
        {
            return new DocumentStoreInMemory(executor);
        }

        protected class TestExecutor : IQueueExecution
        {
            private Queue<IQueuedExecutionDispatcher> _queue = new Queue<IQueuedExecutionDispatcher>();

            public void Enqueue(IQueuedExecutionDispatcher handler)
            {
                _queue.Enqueue(handler);
            }

            public void Process()
            {
                while (_queue.Count > 0)
                    _queue.Dequeue().Execute();
            }
        }
    }
}
