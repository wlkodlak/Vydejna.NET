using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStoreVersionTests
    {
        [TestMethod]
        public void Equality()
        {
            Assert.AreEqual(EventStoreVersion.Any, EventStoreVersion.Any);
            Assert.AreEqual(EventStoreVersion.New, EventStoreVersion.New);
            Assert.AreEqual(EventStoreVersion.At(1), EventStoreVersion.At(1));
            Assert.AreEqual(EventStoreVersion.At(2), EventStoreVersion.At(2));
            Assert.AreEqual(EventStoreVersion.New, EventStoreVersion.At(0));
            Assert.AreNotEqual(EventStoreVersion.Any, EventStoreVersion.New);
            Assert.AreNotEqual(EventStoreVersion.Any, EventStoreVersion.At(0));
            Assert.AreNotEqual(EventStoreVersion.Any, EventStoreVersion.At(1));
            Assert.AreNotEqual(EventStoreVersion.New, EventStoreVersion.Any);
            Assert.AreNotEqual(EventStoreVersion.New, EventStoreVersion.At(1));
            Assert.AreNotEqual(EventStoreVersion.At(2), EventStoreVersion.At(1));
        }

        [TestMethod]
        public void ConvertToString()
        {
            Assert.AreEqual("New", EventStoreVersion.New.ToString());
            Assert.AreEqual("Any", EventStoreVersion.Any.ToString());
            Assert.AreEqual("New", EventStoreVersion.At(0).ToString());
            Assert.AreEqual("Version 47", EventStoreVersion.At(47).ToString());
        }

        [TestMethod]
        public void VerifyVersion_Any()
        {
            Assert.IsTrue(EventStoreVersion.Any.VerifyVersion(0), "0");
            Assert.IsTrue(EventStoreVersion.Any.VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_New()
        {
            Assert.IsTrue(EventStoreVersion.New.VerifyVersion(0), "0");
            Assert.IsFalse(EventStoreVersion.New.VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_At0()
        {
            Assert.IsTrue(EventStoreVersion.At(0).VerifyVersion(0), "0");
            Assert.IsFalse(EventStoreVersion.At(0).VerifyVersion(5), "5");
        }

        [TestMethod]
        public void VerifyVersion_AtVersion()
        {
            Assert.IsFalse(EventStoreVersion.At(5).VerifyVersion(0), "0");
            Assert.IsFalse(EventStoreVersion.At(5).VerifyVersion(3), "3");
            Assert.IsTrue(EventStoreVersion.At(5).VerifyVersion(5), "5");
            Assert.IsFalse(EventStoreVersion.At(5).VerifyVersion(8), "8");
        }

    }
}
