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
        protected TestExecutor Executor;

        [TestInitialize]
        public void Initialize()
        {
            InitializedPartitions = new HashSet<string>();
            Executor = new TestExecutor();
            InitializeCore();
            SetPartition("documents");
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
            SetupDocument("testing", "doc5", "E", new[] { new DocumentIndexing("idxKey", new[] { "E", "F" } ) });
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
            bool isSaved = false;
            Store.GetFolder(folder).SaveDocument(name, contents, DocumentStoreVersion.New, indexes,
                () => { isSaved = true; },
                () => { throw new InvalidOperationException(string.Format("Document {0} already exists", name)); },
                ex => { throw ex.PreserveStackTrace(); });
            Executor.Process();
            Assert.IsTrue(isSaved, "Document {0} setup", name);
        }

        protected void VerifyDocument(string folder, string name, DocumentStoreVersion expectedVersion, string expectedContents)
        {
            string contents = "";
            int version = 0;
            bool failed = false;
            Store.GetFolder(folder).GetDocument(name, (v, c) => { version = v; contents = c; }, () => { }, ex => failed = true);
            Executor.Process();
            Assert.IsFalse(failed, "GetDocument({0}) failed", name);
            if (!expectedVersion.VerifyVersion(version))
                Assert.AreEqual(expectedVersion, DocumentStoreVersion.At(version), "Unexpected version for {0}", name);
            Assert.AreEqual(expectedContents ?? "", contents, "Contents for {0}", name);
        }

        protected void VerifyDocumentKeys(string folder, string indexName, string minValue, string maxValue, IList<string> expectedKeys)
        {
            bool failed = false;
            IList<string> foundKeys = null;
            Store.GetFolder(folder).FindDocumentKeys(indexName, minValue, maxValue, lst => foundKeys = lst, ex => failed = true);
            Executor.Process();
            Assert.IsFalse(failed, "FindDocumentKeys({0},{1},{2}) failed", indexName, minValue, maxValue);
            Assert.IsNotNull(foundKeys, "FindDocumentKeys({0},{1},{2}) null", indexName, minValue, maxValue);
            Assert.AreEqual(string.Join(", ", expectedKeys), string.Join(", ", foundKeys), "FindDocumentKeys({0},{1},{2}) result", indexName, minValue, maxValue);
        }

        protected DocumentStoreFoundDocuments FindDocuments(string folder, string indexName, string minValue, string maxValue, int skip = 0, int maxCount = int.MaxValue, bool ascending = true)
        {
            bool failed = false;
            DocumentStoreFoundDocuments foundKeys = null;
            Store.GetFolder(folder).FindDocuments(indexName, minValue, maxValue, skip, maxCount, ascending, lst => foundKeys = lst, ex => failed = true);
            Executor.Process();
            Assert.IsFalse(failed, "FindDocuments failed");
            Assert.IsNotNull(foundKeys, "FindDocuments null");
            return foundKeys;
        }

        protected void DeleteFolderContents(string folder)
        {
            Store.GetFolder(folder).DeleteAll(() => { }, ex => { });
            Executor.Process();
        }

        protected void SaveDocument(string folder, string name, DocumentStoreVersion expectedVersion, string contents, string expectedOutcome)
        {
            string resultSave = null;
            Store.GetFolder(folder).SaveDocument(name, contents, expectedVersion, null, 
                () => resultSave = "saved", () => resultSave = "conflict", ex => resultSave = "error:" + ex.Message);
            Executor.Process();
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
