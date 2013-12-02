using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Tests.HttpTests
{
    [TestClass, NUnit.Framework.TestFixture]
    public class MissingTests
    {
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpClient_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpServerHeaders_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpServerRequest_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpServerRequestParameter_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpServerRequestRoute_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpRouter_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpRouteConfigurator_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpServer_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpOutputJson_Missing() { }
        [TestMethod, Ignore, NUnit.Framework.Test, NUnit.Framework.Ignore]
        public void HttpOutputDirect_Missing() { }
    }
}
