using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Linq;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpStagedParametersTests
    {
        private HttpServerStagedParameters _parameters;

        [TestInitialize]
        public void Initialize()
        {
            _parameters = new HttpServerStagedParameters();
            _parameters.AddParameter(new RequestParameter(RequestParameterType.Path, "id", "4493"));
            _parameters.AddParameter(new RequestParameter(RequestParameterType.QueryString, "page", "4"));
            _parameters.AddParameter(new RequestParameter(RequestParameterType.QueryString, "filter", "online"));
            _parameters.AddParameter(new RequestParameter(RequestParameterType.QueryString, "filter", "away"));
        }

        [TestMethod]
        public void EnumerateParameters()
        {
            var enumerated = _parameters.ToList();
            Assert.AreEqual(4, enumerated.Count, "Count all");
            Assert.AreEqual("online, away", string.Join(", ", _parameters.Where(p => p.Name == "filter").Select(p => p.Value)), "Filter");
        }

        [TestMethod]
        public void GetMethodReturnsComplexParameter()
        {
            var param = _parameters.Get(RequestParameterType.QueryString, "filter");
            Assert.IsNotNull(param, "Null");
            Assert.AreEqual(RequestParameterType.QueryString, param.Type, "Type");
            Assert.AreEqual("filter", param.Name, "Name");
            Assert.AreEqual("online, away", string.Join(", ", param.GetRawValues()), "Filter");
        }
    }
    [TestClass]
    public class HttpProcessedParameterTests
    {
        [TestMethod]
        public void RawProperties()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "filter", new[] { "online", "away" });
            Assert.AreEqual(RequestParameterType.QueryString, param.Type, "Type");
            Assert.AreEqual("filter", param.Name, "Name");
            Assert.AreEqual("online, away", string.Join(", ", param.GetRawValues()));
        }

        [TestMethod]
        public void ConvertInteger()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "4");
            Assert.AreEqual(4, param.AsInteger().Get());
        }

        [TestMethod]
        public void ConvertString()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "value");
            Assert.AreEqual("value", param.AsString().Get());
        }

        [TestMethod]
        public void ConvertDate()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "2013-10-22");
            Assert.AreEqual(new DateTime(2013, 10, 22), param.As<DateTime>(ConvertDate).Get());
        }

        [TestMethod]
        public void ConvertEmptyDate()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "");
            Assert.IsTrue(param.As<DateTime>(ConvertDate).IsEmpty);
        }

        [TestMethod]
        public void FailedConversionImmediatellyThrows()
        {
            try
            {
                var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "kkd");
                param.As<DateTime>(ConvertDate);
                Assert.Fail("Expected ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        private static DateTime ConvertDate(string date)
        {
            return DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        [TestMethod]
        public void EmptyStringIsSameAsMissing()
        {
            var param = new HttpProcessedParameter(RequestParameterType.QueryString, "page", "");
            Assert.IsTrue(param.AsString().IsEmpty);
        }
    }
    [TestClass]
    public class HttpTypedProcessedParameterTests_Integer
    {
        private IHttpTypedProcessedParameter<int> _intParsed;
        private IHttpTypedProcessedParameter<int> _intZero;
        private IHttpTypedProcessedParameter<int> _intEmpty;

        [TestInitialize]
        public void Initialize()
        {
            _intParsed = HttpTypedProcessedParameter<int>.CreateParsed(RequestParameterType.QueryString, "page", 4);
            _intZero = HttpTypedProcessedParameter<int>.CreateParsed(RequestParameterType.QueryString, "page", 0);
            _intEmpty = HttpTypedProcessedParameter<int>.CreateEmpty(RequestParameterType.QueryString, "page");
        }

        [TestMethod]
        public void GetTypeAndName()
        {
            Assert.AreEqual(RequestParameterType.QueryString, _intParsed.Type, "Type");
            Assert.AreEqual("page", _intParsed.Name, "Name");
        }

        [TestMethod]
        public void GetActualValue()
        {
            Assert.AreEqual(4, _intParsed.Get());
            Assert.AreEqual(0, _intZero.Get());
        }

        [TestMethod]
        public void ParameterIsEmpty()
        {
            Assert.IsFalse(_intParsed.IsEmpty, "Parsed");
            Assert.IsFalse(_intZero.IsEmpty, "Zero");
            Assert.IsTrue(_intEmpty.IsEmpty, "Empty");
        }

        [TestMethod]
        public void GetTypeDefaultValue()
        {
            Assert.AreEqual(0, _intEmpty.Get(), "Type default");
        }

        [TestMethod]
        public void GetManuallDefaultValue()
        {
            Assert.AreEqual(-1, _intEmpty.Default(-1).Get(), "Empty");
            Assert.AreEqual(0, _intZero.Default(-1).Get(), "Zero");
            Assert.AreEqual(4, _intParsed.Default(-1).Get(), "Parsed");
        }

        [TestMethod]
        public void ThrowOnMissingMandatoryParameter()
        {
            try
            {
                _intEmpty.Mandatory().Get();
                Assert.Fail("Expected MissingMandatoryParameterException");
            }
            catch (MissingMandatoryParameterException)
            {
            }
        }

        [TestMethod]
        public void MustNotThrowOnPresentMandatoryParameters()
        {
            _intZero.Mandatory().Get();
            _intParsed.Mandatory().Get();
        }

        [TestMethod]
        public void ValidateByPredicate_Ok()
        {
            _intParsed.Validate(val => val > 0, "must be positive").Get();
        }

        [TestMethod]
        public void ValidateByPredicate_Fail()
        {
            try
            {
                _intParsed.Validate(val => val < 0, "must be negative").Get();
                Assert.Fail("Expected ParameterValidationException");
            }
            catch (ParameterValidationException)
            {
            }
        }

        [TestMethod]
        public void NoValidationOnEmptyParameters()
        {
            _intEmpty.Validate(val => val > 0, "must be positive").Get();
        }

        [TestMethod]
        public void ComplexValidation()
        {
            try
            {
                _intParsed.Validate((param, value) => { throw new FormatException(string.Format("Value {1} of parameter {0} is just wrong", value, param)); }).Get();
                Assert.Fail("Expected FormatException");
            }
            catch (FormatException)
            {
            }
        }
    }
}
