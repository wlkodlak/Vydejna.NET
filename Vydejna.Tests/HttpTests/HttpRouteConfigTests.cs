using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class HttpRouteConfigTests
    {
        private TestAddRoute _router;
        private HttpRouteConfig _cfg;
        private bool _builderUsed;
        private TestBuilder _builder;

        private class TestAddRoute : IHttpAddRoute
        {
            public string Pattern;
            public IHttpRouteHandler Handler;
            public bool MoreThanOne;

            public TestProcessor Processor
            {
                get
                {
                    Assert.IsInstanceOfType(Handler, typeof(TestProcessor));
                    return (TestProcessor)Handler;
                }
            }

            public void AddRoute(string pattern, IHttpRouteHandler handler)
            {
                if (Pattern != null)
                    MoreThanOne = true;
                else
                {
                    Pattern = pattern;
                    Handler = handler;
                }
            }
        }

        public class TestProcessor
            : IHttpRequestDecoder
            , IHttpRequestEnhancer
            , IHttpInputProcessor
            , IHttpPreprocessor
            , IHttpProcessor
            , IHttpPostprocessor
            , IHttpOutputProcessor
            , IHttpRequestEncoder
        {
            public string Name;
            public TestProcessor(string name) { Name = name; }
            public IHttpRequestDecoder Decoder { get { return this; } }
            public IHttpRequestEnhancer Enhancer { get { return this; } }
            public IHttpInputProcessor Input { get { return this; } }
            public IHttpPreprocessor Preprocessor { get { return this; } }
            public IHttpProcessor Processor { get { return this; } }
            public IHttpPostprocessor Postprocessor { get { return this; } }
            public IHttpOutputProcessor Output { get { return this; } }
            public IHttpRequestEncoder Encoder { get { return this; } }

            public Task<HttpServerRequest> Process(HttpServerRequest request)
            {
                throw new NotImplementedException();
            }

            Task<object> IHttpPreprocessor.Process(HttpServerRequest request)
            {
                throw new NotImplementedException();
            }

            Task<HttpServerRequest> IHttpRequestEnhancer.Process(HttpServerRequest request, IEnumerable<RequestParameter> routeParameters)
            {
                throw new NotImplementedException();
            }

            public bool HandlesContentType(string contentType)
            {
                throw new NotImplementedException();
            }

            public Task<object> ProcessInput(HttpServerRequest request)
            {
                throw new NotImplementedException();
            }

            Task<object> IHttpProcessor.Process(HttpServerRequest request)
            {
                throw new NotImplementedException();
            }

            public Task<object> Process(HttpServerRequest request, object response)
            {
                throw new NotImplementedException();
            }

            public bool HandlesOutput(HttpServerRequest request, object response)
            {
                throw new NotImplementedException();
            }

            public Task<HttpServerResponse> ProcessOutput(HttpServerRequest request, object response)
            {
                throw new NotImplementedException();
            }

            public Task<HttpServerResponse> Process(HttpServerResponse request)
            {
                throw new NotImplementedException();
            }
        }

        private class TestBuilder : IHttpStagedHandlerBuilder, IHttpRouteHandler
        {
            public List<IHttpRequestDecoder> Decoders = new List<IHttpRequestDecoder>();
            public List<IHttpRequestEnhancer> Enhancers = new List<IHttpRequestEnhancer>();
            public List<IHttpInputProcessor> Inputs = new List<IHttpInputProcessor>();
            public List<IHttpPreprocessor> Preprocessors = new List<IHttpPreprocessor>();
            public List<IHttpProcessor> Processors = new List<IHttpProcessor>();
            public List<IHttpPostprocessor> Postprocessors = new List<IHttpPostprocessor>();
            public List<IHttpOutputProcessor> Outputs = new List<IHttpOutputProcessor>();
            public List<IHttpRequestEncoder> Encoders = new List<IHttpRequestEncoder>();

            public IHttpStagedHandlerBuilder Add(IHttpRequestDecoder component)
            {
                Decoders.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpRequestEnhancer component)
            {
                Enhancers.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpInputProcessor component)
            {
                Inputs.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpPreprocessor component)
            {
                Preprocessors.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpProcessor component)
            {
                Processors.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpPostprocessor component)
            {
                Postprocessors.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpOutputProcessor component)
            {
                Outputs.Add(component);
                return this;
            }

            public IHttpStagedHandlerBuilder Add(IHttpRequestEncoder component)
            {
                Encoders.Add(component);
                return this;
            }

            public IHttpRouteHandler Build()
            {
                return this;
            }

            public Task<HttpServerResponse> Handle(HttpServerRequest request, IList<RequestParameter> routeParameters)
            {
                throw new NotImplementedException();
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            _builderUsed = false;
            _builder = new TestBuilder();
            _router = new TestAddRoute();
            _cfg = new HttpRouteConfig(_router, BuilderFactory);
        }

        private IHttpStagedHandlerBuilder BuilderFactory()
        {
            Assert.IsFalse(_builderUsed, "Handler was already built");
            return _builder;
        }

        [TestMethod]
        public void RouteDirect()
        {
            var handler = new Mock<IHttpRouteHandler>().Object;
            _cfg.Route("/direct").To(handler);
            _cfg.Configure();
            Assert.AreEqual("/direct", _router.Pattern, "Pattern");
            Assert.AreSame(handler, _router.Handler, "Handler");
            Assert.IsFalse(_router.MoreThanOne, "More than one route added");
        }

        [TestMethod]
        public void RouteStagedWithoutCommon()
        {
            _cfg
                .Route("/staged")
                .With(new TestProcessor("encoder").Encoder)
                .To(new TestProcessor("processor").Processor)
                .With(new TestProcessor("decoder").Decoder)
                .With(new TestProcessor("enhancer").Enhancer);
            _cfg.Configure();
            Assert.AreSame(_builder.Build(), _router.Handler, "Handler");
            AssertTestProcessor(_builder.Decoders, "decoder");
            AssertTestProcessor(_builder.Enhancers, "enhancer");
            AssertTestProcessor(_builder.Inputs);
            AssertTestProcessor(_builder.Preprocessors);
            AssertTestProcessor(_builder.Processors, "processor");
            AssertTestProcessor(_builder.Postprocessors);
            AssertTestProcessor(_builder.Outputs);
            AssertTestProcessor(_builder.Encoders, "encoder");
        }

        [TestMethod]
        public void RouteStagedWithCommon()
        {
            var standard = _cfg
                .Common()
                .With(new TestProcessor("GzipDecoder").Decoder)
                .With(new TestProcessor("GzipEncoder").Encoder)
                .With(new TestProcessor("QueryStringEnhancer").Enhancer)
                .With(new TestProcessor("FormDataEnhancer").Enhancer)
                .With(new TestProcessor("DirectOutput").Output);
            var sessions = _cfg.Common()
                .Using(standard)
                .With(new TestProcessor("SessionPre").Preprocessor)
                .With(new TestProcessor("SessionPost").Postprocessor);
            _cfg
                .Route("/staged")
                .To(new TestProcessor("Processor").Processor)
                .Using(sessions)
                .With(new TestProcessor("JsonInput").Input)
                .With(new TestProcessor("JsonOutput").Output);
            _cfg.Configure();
            Assert.AreSame(_builder.Build(), _router.Handler, "Handler");
            AssertTestProcessor(_builder.Decoders, "GzipDecoder");
            AssertTestProcessor(_builder.Enhancers, "QueryStringEnhancer", "FormDataEnhancer");
            AssertTestProcessor(_builder.Inputs, "JsonInput");
            AssertTestProcessor(_builder.Preprocessors, "SessionPre");
            AssertTestProcessor(_builder.Processors, "Processor");
            AssertTestProcessor(_builder.Postprocessors, "SessionPost");
            AssertTestProcessor(_builder.Outputs, "JsonOutput", "DirectOutput");
            AssertTestProcessor(_builder.Encoders, "GzipEncoder");
        }

        private void AssertTestProcessor<T>(List<T> list, params string[] names)
        {
            CollectionAssert.AllItemsAreInstancesOfType(list, typeof(TestProcessor), "{0}", typeof(T).Name);
            string actual = string.Join(", ", list.Cast<TestProcessor>().Select(p => p.Name));
            string expected = string.Join(", ", names);
            Assert.AreEqual(expected, actual, "{0}", typeof(T).Name);
        }

        private class TestRequest
        {
            public HttpServerRequest Request;
            public TestRequest(HttpServerRequest rq) { Request = rq; }
        }
        private class TestResponse
        {
            public TestRequest TestRequest;
            public TestResponse(TestRequest tr) { TestRequest = tr; }
        }

        [TestMethod]
        public void RouteParametrized()
        {
            var withEnhancer = _cfg.Common()
                .With(new TestProcessor("Enhancer").Enhancer);
            _cfg.Route("/separated")
                .Parametrized<TestRequest>(rq => TaskResult.GetCompletedTask(new TestRequest(rq)))
                .To(tr => TaskResult.GetCompletedTask<object>(new TestResponse(tr)))
                .Using(withEnhancer);
            _cfg.Configure();

            Assert.AreEqual(1, _builder.Processors.Count, "Count");
            var processor = _builder.Processors[0];
            var request = new HttpServerRequest() { Url = "http://localhost/testRequest" };
            var basicResponse = processor.Process(request).GetAwaiter().GetResult();
            Assert.IsInstanceOfType(basicResponse, typeof(TestResponse), "Response type");
            var testResponse = (TestResponse)basicResponse;
            Assert.AreSame(request, testResponse.TestRequest.Request, "Request");
        }
    }
}
