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
        protected string CurrentPartition;
        protected HashSet<string> InitializedPartitions;
        protected IDocumentStore Store;
        protected TestScheduler Scheduler;

        [TestInitialize]
        public void Initialize()
        {
            InitializedPartitions = new HashSet<string>();
            Scheduler = new TestScheduler();
            InitializeCore();
            Scheduler.RunSync(() => SetPartition("documents"));
        }

        protected virtual void InitializeCore() { }

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
        public void DocumentWithSameNameInDifferentPartitionsAreIndependent()
        {
            SetPartition("documents");
            SetupDocument("testing", "document", "original");
            SaveDocument("testing", "document", DocumentStoreVersion.At(1), "first", "saved");
            SetPartition("documents2");
            SaveDocument("testing", "document", DocumentStoreVersion.New, "second", "saved");
            SetPartition("documents");
            VerifyDocument("testing", "document", DocumentStoreVersion.At(2), "first");
        }

        [TestMethod]
        public void FindingKeysInRange()
        {
            SetupDocument("testing", "doc1", "A", new[] { new DocumentIndexing("idxKey", "A") });
            SetupDocument("testing", "doc2", "B", new[] { new DocumentIndexing("idxKey", "B") });
            SetupDocument("testing", "doc3", "C", new[] { new DocumentIndexing("idxKey", "C") });
            SetupDocument("testing", "doc4", "D", new[] { new DocumentIndexing("idxKey", "D") });
            SetupDocument("testing", "doc5", "E", new[] { new DocumentIndexing("idxKey", new[] { "E", "F" }) });
            SetupDocument("testing", "doc6", "G", new[] { new DocumentIndexing("idxKey", "G") });
            VerifyDocumentKeys("testing", "idxKey", "B", "C", new[] { "doc2", "doc3" });
            VerifyDocumentKeys("testing", "idxKey", null, "A", new[] { "doc1" });
            VerifyDocumentKeys("testing", "idxKey", "G", null, new[] { "doc6" });
            VerifyDocumentKeys("testing", "idxKey", "D", "G", new[] { "doc4", "doc5", "doc6" });
        }

        [TestMethod]
        public void FindDocumentsInRange()
        {
            SetupDocument("testing", "doc1", "A", new[] { new DocumentIndexing("idxKey", "A") });
            SetupDocument("testing", "doc2", "B", new[] { new DocumentIndexing("idxKey", "B") });
            SetupDocument("testing", "doc3", "C", new[] { new DocumentIndexing("idxKey", "C") });
            SetupDocument("testing", "doc4", "D", new[] { new DocumentIndexing("idxKey", "D") });
            SetupDocument("testing", "doc5", "E", new[] { new DocumentIndexing("idxKey", "E") });
            Assert.AreEqual("C, D", string.Join(", ", FindDocuments("testing", "idxKey", null, null, 2, 2).Select(d => d.Contents)), "Using skip + maxCount");
            Assert.AreEqual("B, C", string.Join(", ", FindDocuments("testing", "idxKey", "B", "C").Select(d => d.Contents)), "Using index range");
            Assert.AreEqual("E, D", string.Join(", ", FindDocuments("testing", "idxKey", null, null, 0, 2, false).Select(d => d.Contents)), "Descending");
            Assert.AreEqual(4, FindDocuments("testing", "idxKey", "A", "D", 2, 2).TotalFound, "TotalFound keys A-D: range 2-3");
        }

        protected void SetupDocument(string folder, string name, string contents, IList<DocumentIndexing> indexes = null)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).SaveDocument(name, contents, DocumentStoreVersion.New, indexes));
            Assert.IsTrue(task.IsCompleted, "Document {0} setup finished", name);
            Assert.AreEqual(true, task.Result, "Document {0} setup OK", name);
        }

        protected void VerifyDocument(string folder, string name, DocumentStoreVersion expectedVersion, string expectedContents)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).GetDocument(name));
            Assert.IsTrue(task.IsCompleted, "GetDocument({0}) completed", name);
            var version = task.Result.Version;
            var contents = task.Result.Contents;
            if (!expectedVersion.VerifyVersion(version))
                Assert.AreEqual(expectedVersion, DocumentStoreVersion.At(version), "Unexpected version for {0}", name);
            Assert.AreEqual(expectedContents ?? "", contents, "Contents for {0}", name);
        }

        protected void VerifyDocumentKeys(string folder, string indexName, string minValue, string maxValue, IList<string> expectedKeys)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).FindDocumentKeys(indexName, minValue, maxValue));
            Assert.IsTrue(task.IsCompleted, "FindDocumentKeys({0},{1},{2}) complete", indexName, minValue, maxValue);
            var foundKeys = task.Result;
            Assert.IsNotNull(foundKeys, "FindDocumentKeys({0},{1},{2}) null", indexName, minValue, maxValue);
            Assert.AreEqual(string.Join(", ", expectedKeys), string.Join(", ", foundKeys), "FindDocumentKeys({0},{1},{2}) result", indexName, minValue, maxValue);
        }

        protected DocumentStoreFoundDocuments FindDocuments(string folder, string indexName, string minValue, string maxValue, int skip = 0, int maxCount = int.MaxValue, bool ascending = true)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).FindDocuments(indexName, minValue, maxValue, skip, maxCount, ascending));
            Assert.IsTrue(task.IsCompleted, "FindDocuments complete");
            var foundKeys = task.Result;
            Assert.IsNotNull(foundKeys, "FindDocuments null");
            return foundKeys;
        }

        protected void DeleteFolderContents(string folder)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).DeleteAll());
            Assert.IsTrue(task.IsCompleted, "FindDocuments complete");
        }

        protected void SaveDocument(string folder, string name, DocumentStoreVersion expectedVersion, string contents, string expectedOutcome)
        {
            var task = Scheduler.Run(() => Store.GetFolder(folder).SaveDocument(name, contents, expectedVersion, null));
            Assert.IsTrue(task.IsCompleted, "SaveDocument complete");
            var resultSave = task.Exception != null ? "error: " + task.Exception.InnerException.Message : task.Result ? "saved" : "conflict";
            Assert.AreEqual(expectedOutcome, resultSave, "Outcome for {0}", name);
        }

        protected void SetPartition(string name)
        {
            if (name == CurrentPartition)
                return;
            CurrentPartition = name;
            if (InitializedPartitions.Add(name))
                Store = CreateEmptyDocumentStore();
            else
                Store = GetExistingDocumentStore();
        }

        protected abstract IDocumentStore GetExistingDocumentStore();
        protected abstract IDocumentStore CreateEmptyDocumentStore();
    }
}
