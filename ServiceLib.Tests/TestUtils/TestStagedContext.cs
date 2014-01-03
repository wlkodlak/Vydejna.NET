using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestStagedContext : IHttpServerStagedContext
    {
        public static TestStagedContext Get(string pathAndQuery)
        {
            throw new NotImplementedException();
        }

        public string Method
        {
            get { throw new NotImplementedException(); }
        }

        public string Url
        {
            get { throw new NotImplementedException(); }
        }

        public string ClientAddress
        {
            get { throw new NotImplementedException(); }
        }

        public string InputString
        {
            get { throw new NotImplementedException(); }
        }

        public int StatusCode
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public string OutputString
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public IHttpSerializer InputSerializer
        {
            get { throw new NotImplementedException(); }
        }

        public IHttpSerializer OutputSerializer
        {
            get { throw new NotImplementedException(); }
        }

        public IHttpServerStagedHeaders InputHeaders
        {
            get { throw new NotImplementedException(); }
        }

        public IHttpServerStagedHeaders OutputHeaders
        {
            get { throw new NotImplementedException(); }
        }

        public IEnumerable<RequestParameter> RawParameters
        {
            get { throw new NotImplementedException(); }
        }

        public IHttpProcessedParameter Parameter(string name)
        {
            throw new NotImplementedException();
        }

        public IHttpProcessedParameter PostData(string name)
        {
            throw new NotImplementedException();
        }

        public IHttpProcessedParameter Route(string name)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }
    }
}
