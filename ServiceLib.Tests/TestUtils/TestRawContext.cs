using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ServiceLib.Tests.TestUtils
{
    public class TestRawContext : IHttpServerRawContext
    {
        private string _method, _url, _clientIp;
        private MemoryStream _input, _output;
        private ManualResetEventSlim _mre;

        public TestRawContext(string method, string url, string clientIp)
        {
            _method = method;
            _url = url;
            _clientIp = clientIp;
            _input = new MemoryStream();
            _output = new MemoryStream();
            InputHeaders = new TestHeaders();
            OutputHeaders = new TestHeaders();
            RouteParameters = new List<RequestParameter>();
            _mre = new ManualResetEventSlim();
        }
        public string Method { get { return _method; } }
        public string Url { get { return _url; } }
        public string ClientAddress { get { return _clientIp; } }
        public int StatusCode { get; set; }
        public Stream InputStream { get { return _input; } }
        public Stream OutputStream { get { return _output; } }
        public IList<RequestParameter> RouteParameters { get; private set; }
        public IHttpServerRawHeaders InputHeaders { get; private set; }
        public IHttpServerRawHeaders OutputHeaders { get; private set; }
        public void Close()
        {
            _mre.Set();
        }
        public bool WaitForClose()
        {
            return _mre.Wait(100);
        }
        public byte[] GetRawOutput()
        {
            return _output.ToArray();
        }

        private class TestHeaders : IHttpServerRawHeaders
        {
            private List<KeyValuePair<string, string>> _data = new List<KeyValuePair<string, string>>();
            public void Add(string name, string value) { _data.Add(new KeyValuePair<string, string>(name, value)); }
            public void Clear() { _data.Clear(); }
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() { return _data.GetEnumerator(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        public void SetInput(byte[] input)
        {
            _input.Write(input, 0, input.Length);
            _input.Seek(0, SeekOrigin.Begin);
        }
    }
}
