using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ServiceLib.Tests.Logging
{
    [TestClass]
    public class LogContextTests
    {
        private ILogContextFactory _factory;
        
        [TestInitialize]
        public void Initialize()
        {
            _factory = new LogContextFactory();
        }

        private class TestLogContext1
        {
        }

        private class TestLogContext2
        {
        }

        [TestMethod]
        public void EmptyContext()
        {
            var ctx = _factory.Build("3948");
            Assert.AreEqual("3948", ctx.ShortContext);
            Assert.IsNull(ctx.GetContext<TestLogContext1>());
        }

        [TestMethod]
        public void WithTestContext()
        {
            var ctx = _factory.Build("vkdjur");
            var test1 = new TestLogContext1();
            var test2 = new TestLogContext2();
            ctx.SetContext(test1);
            ctx.SetContext(test2);
            Assert.AreSame(test1, ctx.GetContext<TestLogContext1>());
            Assert.AreSame(test2, ctx.GetContext<TestLogContext2>());
        }
    }
}
