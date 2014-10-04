using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ServiceLib.Tests.Logging
{
    [TestClass]
    public class LogContextSummaryParserTests
    {
        private LogContextSummaryParser _parser;
        
        [TestMethod]
        public void JustFixedText()
        {
            Parse("This is a message.");
            ExpectFixed("This is a message.");
            ExpectEnd();
        }

        [TestMethod]
        public void JustVariable()
        {
            Parse("{Name}");
            ExpectVariable("Name");
            ExpectEnd();
        }

        [TestMethod]
        public void JustEscapedStart()
        {
            Parse("{{");
            ExpectFixed("{");
            ExpectEnd();
        }

        [TestMethod]
        public void JustEscapedEndUsingStartMethod()
        {
            Parse("{}");
            ExpectFixed("}");
            ExpectEnd();
        }

        [TestMethod]
        public void JustEscapedEndUsingDoubleEnd()
        {
            Parse("}}");
            ExpectFixed("}");
            ExpectEnd();
        }

        [TestMethod]
        public void SimpleCombination()
        {
            Parse("Event {Id} arrived");
            ExpectFixed("Event ");
            ExpectVariable("Id");
            ExpectFixed(" arrived");
        }

        [TestMethod]
        public void ComplexCombination()
        {
            Parse("Start {{ with {} and }}. Variable {Help}{Me}!");
            ExpectFixed("Start ");
            ExpectFixed("{");
            ExpectFixed(" with ");
            ExpectFixed("}");
            ExpectFixed(" and ");
            ExpectFixed("}");
            ExpectFixed(". Variable ");
            ExpectVariable("Help");
            ExpectVariable("Me");
            ExpectFixed("!");
        }

        private void Parse(string format)
        {
            _parser = new LogContextSummaryParser(format);
        }

        private void ExpectFixed(string text)
        {
            Assert.IsTrue(_parser.MoveNext(), "Expected token");
            var element = _parser.Current;
            Assert.IsNotNull(element, "Element NULL");
            Assert.AreEqual(text, element.FixedText, "FixedText");
            Assert.IsNull(element.PropertyName, "PropertyName");
        }

        private void ExpectVariable(string name)
        {
            Assert.IsTrue(_parser.MoveNext(), "Expected token");
            var element = _parser.Current;
            Assert.IsNotNull(element, "Element NULL");
            Assert.IsNull(element.FixedText, "FixedText");
            Assert.AreEqual(name, element.PropertyName, "PropertyName");
        }

        private void ExpectEnd()
        {
            Assert.IsFalse(_parser.MoveNext(), "Expected end");
            Assert.IsNull(_parser.Current, "Element");
        }
    }
}
