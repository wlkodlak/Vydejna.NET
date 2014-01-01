using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class EventStoreTokenTests
    {
        [TestMethod]
        public void Equality()
        {
            Assert.AreEqual(EventStoreToken.Initial, EventStoreToken.Initial);
            Assert.AreEqual(EventStoreToken.Initial, new EventStoreToken(""));
            Assert.AreEqual(EventStoreToken.Current, EventStoreToken.Current);
            Assert.AreEqual(new EventStoreToken("55"), new EventStoreToken("55"));
            Assert.AreNotEqual(EventStoreToken.Initial, EventStoreToken.Current);
            Assert.AreNotEqual(EventStoreToken.Initial, new EventStoreToken("55"));
            Assert.AreNotEqual(EventStoreToken.Current, new EventStoreToken("55"));
            Assert.AreNotEqual(new EventStoreToken("88"), new EventStoreToken("55"));
        }

        [TestMethod]
        public void Flags()
        {
            Assert.IsTrue(EventStoreToken.Initial.IsInitial, "Initial.IsInitial");
            Assert.IsFalse(EventStoreToken.Initial.IsCurrent, "Initial.IsCurrent");
            Assert.IsFalse(EventStoreToken.Current.IsInitial, "Current.IsInitial");
            Assert.IsTrue(EventStoreToken.Current.IsCurrent, "Current.IsCurrent");
        }

        [TestMethod]
        public void ConvertToString()
        {
            Assert.AreEqual("", EventStoreToken.Initial.ToString(), "Initial");
            Assert.AreEqual("55", new EventStoreToken("55").ToString(), "55");
        }

        [TestMethod]
        public void CompareInitial()
        {
            AssertCompare(0, EventStoreToken.Initial, EventStoreToken.Initial);
            AssertCompare(-1, EventStoreToken.Initial, EventStoreToken.Current);
            AssertCompare(-1, EventStoreToken.Initial, new EventStoreToken("55"));
            AssertCompare(1, EventStoreToken.Current, EventStoreToken.Initial);
            AssertCompare(1, new EventStoreToken("55"), EventStoreToken.Initial);
        }

        [TestMethod]
        public void CompareCurrent()
        {
            AssertCompare(0, EventStoreToken.Current, EventStoreToken.Current);
            AssertCompare(-1, EventStoreToken.Initial, EventStoreToken.Current);
            AssertCompare(1, EventStoreToken.Current, new EventStoreToken("55"));
            AssertCompare(1, EventStoreToken.Current, EventStoreToken.Initial);
            AssertCompare(-1, new EventStoreToken("55"), EventStoreToken.Current);
        }

        [TestMethod]
        public void CompareNumbered()
        {
            AssertCompare(0, new EventStoreToken("2"), new EventStoreToken("2"));
            AssertCompare(0, new EventStoreToken(""), new EventStoreToken(""));
            AssertCompare(0, new EventStoreToken("5"), new EventStoreToken("5"));
            AssertCompare(-1, new EventStoreToken(""), new EventStoreToken("2"));
            AssertCompare(-1, new EventStoreToken("2"), new EventStoreToken("5"));
            AssertCompare(-1, new EventStoreToken("2"), new EventStoreToken("10"));
            AssertCompare(1, new EventStoreToken("2"), new EventStoreToken(""));
            AssertCompare(1, new EventStoreToken("5"), new EventStoreToken("2"));
            AssertCompare(1, new EventStoreToken("10"), new EventStoreToken("5"));
        }

        private void AssertCompare(int expected, EventStoreToken a, EventStoreToken b)
        {
            Assert.AreEqual(expected, Math.Sign(EventStoreToken.Compare(a, b)), "{0} <=> {1}", a, b);
        }
    }
}
