using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpRouteConfig
    {
        private IHttpAddRoute _router;
        private Func<IHttpStagedHandlerBuilder> _builderFactory;
        private List<Configurator> _pending;

        public HttpRouteConfig(IHttpAddRoute router, Func<IHttpStagedHandlerBuilder> builderFactory)
        {
            _router = router;
            _builderFactory = builderFactory;
            _pending = new List<Configurator>();
        }
        public IHttpRouteConfigRouteWithDirect Route(string pattern)
        {
            var cfg = new Configurator(pattern);
            _pending.Add(cfg);
            return cfg;
        }
        public IHttpRouteConfigCommon Common()
        {
            return new Configurator(null);
        }
        public void Configure()
        {
            foreach (var cfg in _pending)
            {
                cfg.Configure(_router, _builderFactory);
            }
        }

        private class Configurator<T> : IHttpRouteConfigParametrized<T>
        {
            private IHttpRouteConfigRouteWithDirect _parent;
            private Func<HttpServerRequest, Task<T>> _translator;

            public Configurator(IHttpRouteConfigRouteWithDirect parent, Func<HttpServerRequest, Task<T>> translator)
            {
                _parent = parent;
                _translator = translator;
            }

            public IHttpRouteConfigComponents To(Func<T, Task<object>> handler)
            {
                return _parent.To(new ParametrizedProcessor<T>(_translator, handler));
            }
        }

        private class ParametrizedProcessor<T> : IHttpProcessor
        {
            private Func<HttpServerRequest, Task<T>> _translator;
            private Func<T, Task<object>> _handler;
           
            public ParametrizedProcessor(Func<HttpServerRequest, Task<T>> translator, Func<T, Task<object>> handler)
            {
                _translator = translator;
                _handler = handler;
            }

            public async Task<object> Process(HttpServerRequest request)
            {
                var typedRequest = await _translator(request).ConfigureAwait(false);
                var result = await _handler(typedRequest).ConfigureAwait(false);
                return result;
            }
        }

        private class DelegatedProcessor : IHttpProcessor
        {
            private Func<HttpServerRequest, Task<object>> _handler;

            public DelegatedProcessor(Func<HttpServerRequest, Task<object>> handler)
            {
                _handler = handler;
            }

            public Task<object> Process(HttpServerRequest request)
            {
                return _handler(request);
            }
        }

        private class Configurator : IHttpRouteConfigRouteWithDirect, IHttpRouteConfigCommon, IHttpRouteConfigComponents
        {
            private string _pattern;
            private List<string> _prefixes = new List<string>();

            private List<Configurator> _using = new List<Configurator>();
            private List<IHttpRequestDecoder> _decoders = new List<IHttpRequestDecoder>();
            private List<IHttpRequestEnhancer> _enhancers = new List<IHttpRequestEnhancer>();
            private List<IHttpInputProcessor> _inputs = new List<IHttpInputProcessor>();
            private List<IHttpPreprocessor> _pre = new List<IHttpPreprocessor>();
            private IHttpProcessor _processor;
            private List<IHttpPostprocessor> _post = new List<IHttpPostprocessor>();
            private List<IHttpOutputProcessor> _outputs = new List<IHttpOutputProcessor>();
            private List<IHttpRequestEncoder> _encoders = new List<IHttpRequestEncoder>();
            private IHttpRouteHandler _handler;

            public Configurator(string pattern)
            {
                _pattern = pattern;
            }

            public void Configure(IHttpAddRoute router, Func<IHttpStagedHandlerBuilder> factory)
            {
                if (_handler != null)
                    router.AddRoute(_pattern, _handler);
                else
                {
                    var builder = factory();
                    FillDecoders(builder);
                    FillEnhancers(builder);
                    FillInputs(builder);
                    FillPre(builder);
                    builder.Add(_processor);
                    FillPost(builder);
                    FillOutputs(builder);
                    FillEncoders(builder);
                    router.AddRoute(_pattern, builder.Build(), _prefixes);
                }
            }

            private void FillDecoders(IHttpStagedHandlerBuilder builder)
            {
                for (int i = 0; i < _using.Count; i++)
                    _using[i].FillDecoders(builder);
                for (int i = 0; i < _decoders.Count; i++)
                    builder.Add(_decoders[i]);
            }

            private void FillEnhancers(IHttpStagedHandlerBuilder builder)
            {
                for (int i = 0; i < _using.Count; i++)
                    _using[i].FillEnhancers(builder);
                for (int i = 0; i < _enhancers.Count; i++)
                    builder.Add(_enhancers[i]);
            }

            private void FillInputs(IHttpStagedHandlerBuilder builder)
            {
                for (int i = _inputs.Count - 1; i >= 0 ; i--)
                    builder.Add(_inputs[i]);
                for (int i = _using.Count - 1; i >= 0; i--)
                    _using[i].FillInputs(builder);
            }

            private void FillPre(IHttpStagedHandlerBuilder builder)
            {
                for (int i = 0; i < _using.Count; i++)
                    _using[i].FillPre(builder);
                for (int i = 0; i < _pre.Count; i++)
                    builder.Add(_pre[i]);
            }

            private void FillPost(IHttpStagedHandlerBuilder builder)
            {
                for (int i = _post.Count - 1; i >= 0; i--)
                    builder.Add(_post[i]);
                for (int i = _using.Count - 1; i >= 0; i--)
                    _using[i].FillPost(builder);
            }

            private void FillOutputs(IHttpStagedHandlerBuilder builder)
            {
                for (int i = _outputs.Count - 1; i >= 0; i--)
                    builder.Add(_outputs[i]);
                for (int i = _using.Count - 1; i >= 0; i--)
                    _using[i].FillOutputs(builder);
            }

            private void FillEncoders(IHttpStagedHandlerBuilder builder)
            {
                for (int i = _encoders.Count - 1; i >= 0; i--)
                    builder.Add(_encoders[i]);
                for (int i = _using.Count - 1; i >= 0; i--)
                    _using[i].FillEncoders(builder);
            }

            private Configurator Clean()
            {
                _decoders.Clear();
                _encoders.Clear();
                _enhancers.Clear();
                _inputs.Clear();
                _outputs.Clear();
                _post.Clear();
                _pre.Clear();
                _processor = null;
                _using.Clear();
                return this;
            }

            void IHttpRouteConfigRouteWithDirect.To(IHttpRouteHandler directHandler)
            {
                _handler = directHandler;
            }

            IHttpRouteConfigRoute IHttpRouteConfigRoute.Clean()
            {
                return Clean();
            }

            IHttpRouteConfigComponents IHttpRouteConfigRoute.To(IHttpProcessor processor)
            {
                _processor = processor;
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigRoute.To(Func<HttpServerRequest, Task<object>> handler)
            {
                _processor = new DelegatedProcessor(handler);
                return this;
            }

            IHttpRouteConfigParametrized<T> IHttpRouteConfigRoute.Parametrized<T>(Func<HttpServerRequest, Task<T>> translator)
            {
                return new Configurator<T>(this, translator);
            }

            IHttpRouteConfigRoute IHttpRouteConfigRoute.Prefixed(string prefix)
            {
                _prefixes.Add(prefix);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpRequestDecoder handler)
            {
                _decoders.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpRequestEncoder handler)
            {
                _encoders.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpRequestEnhancer handler)
            {
                _enhancers.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpInputProcessor handler)
            {
                _inputs.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpOutputProcessor handler)
            {
                _outputs.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpPreprocessor handler)
            {
                _pre.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.With(IHttpPostprocessor handler)
            {
                _post.Add(handler);
                return this;
            }

            IHttpRouteConfigRoute IHttpRouteConfigComponents<IHttpRouteConfigRoute>.Using(IHttpRouteConfigCommon common)
            {
                _using.Add((Configurator)common);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpRequestDecoder handler)
            {
                _decoders.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpRequestEncoder handler)
            {
                _encoders.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpRequestEnhancer handler)
            {
                _enhancers.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpInputProcessor handler)
            {
                _inputs.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpOutputProcessor handler)
            {
                _outputs.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpPreprocessor handler)
            {
                _pre.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.With(IHttpPostprocessor handler)
            {
                _post.Add(handler);
                return this;
            }

            IHttpRouteConfigCommon IHttpRouteConfigComponents<IHttpRouteConfigCommon>.Using(IHttpRouteConfigCommon common)
            {
                _using.Add((Configurator)common);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents.Clean()
            {
                return Clean();
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpRequestDecoder handler)
            {
                _decoders.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpRequestEncoder handler)
            {
                _encoders.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpRequestEnhancer handler)
            {
                _enhancers.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpInputProcessor handler)
            {
                _inputs.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpOutputProcessor handler)
            {
                _outputs.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpPreprocessor handler)
            {
                _pre.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.With(IHttpPostprocessor handler)
            {
                _post.Add(handler);
                return this;
            }

            IHttpRouteConfigComponents IHttpRouteConfigComponents<IHttpRouteConfigComponents>.Using(IHttpRouteConfigCommon common)
            {
                _using.Add((Configurator)common);
                return this;
            }
        }
    }

    public interface IHttpRouteConfigComponents<T>
    {
        T With(IHttpRequestDecoder handler);
        T With(IHttpRequestEncoder handler);
        T With(IHttpRequestEnhancer handler);
        T With(IHttpInputProcessor handler);
        T With(IHttpOutputProcessor handler);
        T With(IHttpPreprocessor handler);
        T With(IHttpPostprocessor handler);
        T Using(IHttpRouteConfigCommon common);
    }

    public interface IHttpRouteConfigRoute : IHttpRouteConfigComponents<IHttpRouteConfigRoute>
    {
        IHttpRouteConfigRoute Clean();
        IHttpRouteConfigComponents To(IHttpProcessor processor);
        IHttpRouteConfigComponents To(Func<HttpServerRequest, Task<object>> handler);
        IHttpRouteConfigParametrized<T> Parametrized<T>(Func<HttpServerRequest, Task<T>> translator);
        IHttpRouteConfigRoute Prefixed(string prefix);
    }

    public interface IHttpRouteConfigParametrized<T>
    {
        IHttpRouteConfigComponents To(Func<T, Task<object>> handler);
    }

    public interface IHttpRouteConfigRouteWithDirect : IHttpRouteConfigRoute
    {
        void To(IHttpRouteHandler directHandler);
    }

    public interface IHttpRouteConfigCommon : IHttpRouteConfigComponents<IHttpRouteConfigCommon>
    {
    }

    public interface IHttpRouteConfigComponents : IHttpRouteConfigComponents<IHttpRouteConfigComponents>
    {
        IHttpRouteConfigComponents Clean();
    }
}
