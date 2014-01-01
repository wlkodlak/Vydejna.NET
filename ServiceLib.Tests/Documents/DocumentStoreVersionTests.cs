using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.Documents
{
    [TestClass]
    public class DocumentStoreVersionTests
    {
        [TestMethod]
        public void Equality()
        {
            Assert.AreEqual(DocumentStoreVersion.Any, DocumentStoreVersion.Any);
            Assert.AreEqual(DocumentStoreVersion.New, DocumentStoreVersion.New);
            Assert.AreEqual(DocumentStoreVersion.At(1), DocumentStoreVersion.At(1));
            Assert.AreEqual(DocumentStoreVersion.At(2), DocumentStoreVersion.At(2));
            Assert.AreEqual(DocumentStoreVersion.New, DocumentStoreVersion.At(0));
            Assert.AreNotEqual(DocumentStoreVersion.Any, DocumentStoreVersion.New);
            Assert.AreNotEqual(DocumentStoreVersion.Any, DocumentStoreVersion.At(0));
            Assert.AreNotEqual(DocumentStoreVersion.Any, DocumentStoreVersion.At(1));
            Assert.AreNotEqual(DocumentStoreVersion.New, DocumentStoreVersion.Any);
            Assert.AreNotEqual(DocumentStoreVersion.New, DocumentStoreVersion.At(1));
            Assert.AreNotEqual(DocumentStoreVersion.At(2), DocumentStoreVersion.At(1));
        }

        [TestMethod]
        public void ConvertToString()
        {
            Assert.AreEqual("New", DocumentStoreVersion.New.ToString());
            Assert.AreEqual("Any", DocumentStoreVersion.Any.ToString());
            Assert.AreEqual("New", DocumentStoreVersion.At(0).ToString());
            Assert.AreEqual("Version 47", DocumentStoreVersion.At(47).ToString());
        }

        [TestMethod]
        public void AllowNew()
        {
            Assert.IsTrue(DocumentStoreVersion.Any.AllowNew, "Any");
            Assert.IsTrue(DocumentStoreVersion.New.AllowNew, "New");
            Assert.IsTrue(DocumentStoreVersion.At(0).AllowNew, "At(0)");
            Assert.IsFalse(DocumentStoreVersion.At(4).AllowNew, "At(4)");
        }

        [TestMethod]
        public void VerifyVersion_Any()
        {
            Assert.IsTrue(DocumentStoreVersion.Any.VerifyVersion(0), "0");
            Assert.IsTrue(DocumentStoreVersion.Any.VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_New()
        {
            Assert.IsTrue(DocumentStoreVersion.New.VerifyVersion(0), "0");
            Assert.IsFalse(DocumentStoreVersion.New.VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_At0()
        {
            Assert.IsTrue(DocumentStoreVersion.At(0).VerifyVersion(0), "0");
            Assert.IsFalse(DocumentStoreVersion.At(0).VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_AtVersion()
        {
            Assert.IsFalse(DocumentStoreVersion.At(5).VerifyVersion(0), "0");
            Assert.IsFalse(DocumentStoreVersion.At(5).VerifyVersion(3), "3");
            Assert.IsTrue(DocumentStoreVersion.At(5).VerifyVersion(5), "5");
            Assert.IsFalse(DocumentStoreVersion.At(5).VerifyVersion(8), "8");
        }
    }
}
