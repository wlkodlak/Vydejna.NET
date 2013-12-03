using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class MissingTests
    {
        [TestMethod, Ignore]
        public void HttpClient_Missing() { }
        [TestMethod, Ignore]
        public void HttpServer_Missing() { }
        [TestMethod, Ignore]
        public void HttpRouter_Missing() { }
        [TestMethod, Ignore]
        public void HttpRouteConfigurator_Missing() { }

        [TestMethod, Ignore]
        public void HttpOutputDirect_Missing() { }
    }
}
