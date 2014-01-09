using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public abstract class DocumentStoreTestBase
    {
        protected IDocumentStore Store;
        protected TestExecutor Executor;

        [TestInitialize]
        public void Initialize()
        {
            Executor = new TestExecutor();
            Store = CreateEmptyDocumentStore(Executor);
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
            DeleteFolderContents("testing");
            VerifyDocument("testing", "document", DocumentStoreVersion.New, null);
        }

        [TestMethod]
        public void WatchChanges_SameDocument()
        {
            SetupDocument("testing", "document", "original");
            int changes = 0;
            var mre = new ManualResetEventSlim();
            Store.SubFolder("testing").WatchChanges("document", () => { changes++; mre.Set(); });
            SaveDocument("testing", "document", DocumentStoreVersion.Any, "new contents", "saved");
            mre.Wait(500);
            Assert.AreEqual(1, changes, "Changes count");
        }

        [TestMethod]
        public void WatchChanges_DifferentDocument()
        {
            SetupDocument("testing", "document1", "A");
            SetupDocument("testing", "document2", "B");
            int changes = 0;
            var mre = new ManualResetEventSlim();
            Store.SubFolder("testing").WatchChanges("document1", () => { changes++; mre.Set(); });
            SaveDocument("testing", "document2", DocumentStoreVersion.Any, "new contents", "saved");
            mre.Wait(500);
            Assert.AreEqual(0, changes, "Changes count");
        }

        protected void SetupDocument(string folder, string name, string contents)
        {
            Store.SubFolder(folder).SaveDocument(name, contents, DocumentStoreVersion.Any,
                () => { }, () => { }, ex => { });
            Executor.Process();
        }

        protected void VerifyDocument(string folder, string name, DocumentStoreVersion expectedVersion, string expectedContents)
        {
            string contents = "";
            int version = 0;
            bool failed = false;
            Store.SubFolder(folder).GetDocument(name, (v, c) => { version = v; contents = c; }, () => { }, ex => failed = true);
            Executor.Process();
            Assert.IsFalse(failed, "GetDocument({0}) failed", name);
            if (!expectedVersion.VerifyVersion(version))
                Assert.AreEqual(expectedVersion, DocumentStoreVersion.At(version), "Unexpected version for {0}", name);
            Assert.AreEqual(expectedContents ?? "", contents, "Contents for {0}", name);
        }

        protected void DeleteFolderContents(string folder)
        {
            Store.SubFolder(folder).DeleteAll(() => { }, ex => { });
            Executor.Process();
        }

        protected void SaveDocument(string folder, string name, DocumentStoreVersion expectedVersion, string contents, string expectedOutcome)
        {
            string resultSave = null;
            Store.SubFolder(folder).SaveDocument(name, contents, expectedVersion,
                () => resultSave = "saved", () => resultSave = "conflict", ex => resultSave = "error");
            Executor.Process();
            Assert.AreEqual(expectedOutcome, resultSave, "Outcome for {0}", name);
        }

        protected abstract IDocumentStore CreateEmptyDocumentStore(IQueueExecution executor);
    }
}
