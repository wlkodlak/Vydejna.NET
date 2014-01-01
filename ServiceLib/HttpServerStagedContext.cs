using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpServerStagedContext : IHttpServerStagedContext
    {
        private IHttpServerRawContext _rawContext;
        private HttpServerStagedParameters _parameters;

        public HttpServerStagedContext(IHttpServerRawContext context)
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
        }

        public void LoadParameters()
        {
            foreach (var parameter in _rawContext.RouteParameters)
                _parameters.AddParameter(parameter);
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
        public IProcessedParameter Parameter(string name)
        {
            return _parameters.Get(RequestParameterType.QueryString, name);
        }
        public IProcessedParameter PostData(string name)
        {
            return _parameters.Get(RequestParameterType.PostData, name);
        }
        public IProcessedParameter Route(string name)
        {
            return _parameters.Get(RequestParameterType.Path, name);
        }
        public void Close()
        {
            Task.Factory.StartNew(FinishContext);
        }
        private void FinishContext()
        {
            try
            {
                _rawContext.StatusCode = StatusCode;
                _rawContext.OutputHeaders.Clear();
                foreach (var header in OutputHeaders)
                    _rawContext.OutputHeaders.Add(header.Key, header.Value);
                if (!string.IsNullOrEmpty(OutputString))
                {
                    using (var writer = new StreamWriter(_rawContext.OutputStream))
                        writer.Write(OutputString);
                }
            }
            catch
            {
                _rawContext.StatusCode = 500;
            }
            finally
            {
                _rawContext.Close();
            }
        }
    }
}
