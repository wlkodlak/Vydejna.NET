using System.Collections.Generic;

namespace ServiceLib
{
    public class HttpServerStagedContext : IHttpServerStagedContext
    {
        private readonly IHttpServerRawContext _rawContext;
        private readonly HttpServerStagedParameters _parameters;

        public HttpServerStagedContext(IHttpServerRawContext context, IList<RequestParameter> routeParameters)
        {
            _rawContext = context;
            Method = context.Method;
            Url = context.Url;
            ClientAddress = context.ClientAddress;
            InputHeaders = new HttpServerStagedContextHeaders();
            foreach (var item in context.InputHeaders)
                InputHeaders.Add(item.Key, item.Value);
            OutputHeaders = new HttpServerStagedContextHeaders();
            _parameters = new HttpServerStagedParameters();
            foreach (var parameter in routeParameters)
                _parameters.AddParameter(parameter);
        }

        public void LoadParameters()
        {
            foreach (var parameter in ParametrizedUrl.ParseQueryString(_rawContext.Url))
                _parameters.AddParameter(parameter);
        }

        public string Method { get; private set; }
        public string Url { get; private set; }
        public string ClientAddress { get; private set; }
        public string InputString { get; set; }
        public int StatusCode { get; set; }
        public string OutputString { get; set; }
        public IHttpSerializer InputSerializer { get; set; }
        public IHttpSerializer OutputSerializer { get; set; }
        public IHttpServerStagedHeaders InputHeaders { get; private set; }
        public IHttpServerStagedHeaders OutputHeaders { get; private set; }
        
        public IEnumerable<RequestParameter> RawParameters
        {
            get { return _parameters; }
        }
        
        public IHttpProcessedParameter Parameter(string name)
        {
            return _parameters.Get(RequestParameterType.QueryString, name);
        }
       
        public IHttpProcessedParameter PostData(string name)
        {
            return _parameters.Get(RequestParameterType.PostData, name);
        }
     
        public IHttpProcessedParameter Route(string name)
        {
            return _parameters.Get(RequestParameterType.Path, name);
        }
    }
}
