using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpServerDispatcherTests
    {
        private Mock<IHttpRouter> _router;
        private HttpServerDispatcher _dispatcher;
        private Mock<IHttpServerRawContext> _context;
        
        [TestInitialize]
        public void Initialize()
        {
            _router = new Mock<IHttpRouter>();
            _context = new Mock<IHttpServerRawContext>();
            _dispatcher = new HttpServerDispatcher(_router.Object);
        }

        [TestMethod]
        public void NotFound()
        {
            _router.Setup(r => r.FindRoute("http://localhost/path/to/resource")).Returns((HttpUsedRoute)null);
            _context.Setup(x => x.Url).Returns("http://localhost/path/to/resource");
            _context.SetupProperty(x => x.StatusCode, 200);
            var task = _dispatcher.DispatchRequest(_context.Object);
            task.Wait(500);
            Assert.AreEqual(404, _context.Object.StatusCode, "StatusCode");
            _context.Verify();
        }

        [TestMethod]
        public void ServerError()
        {
            var handler = new Mock<IHttpRouteHandler>();
            handler.Setup(x => x.Handle(_context.Object)).Throws(new InvalidOperationException("Some error"));
            var route = new HttpUsedRoute(new ParametrizedUrl("/path/to/resource"), handler.Object);
            _router.Setup(r => r.FindRoute("http://localhost/path/to/resource")).Returns(route);
            _context.Setup(x => x.Url).Returns("http://localhost/path/to/resource");
            _context.SetupProperty(x => x.StatusCode, 200);
            var task = _dispatcher.DispatchRequest(_context.Object);
            task.Wait(500);
            Assert.AreEqual(500, _context.Object.StatusCode, "StatusCode");
            _context.Verify();
        }

        [TestMethod]
        public void PassToHandler()
        {
            var handler = new Mock<IHttpRouteHandler>();
            handler.Setup(x => x.Handle(_context.Object)).Verifiable();
            var route = new HttpUsedRoute(new ParametrizedUrl("/path/to/resource"), handler.Object);
            _router.Setup(r => r.FindRoute("http://localhost/path/to/resource")).Returns(route);
            _context.Setup(x => x.Url).Returns("http://localhost/path/to/resource");
            _dispatcher.DispatchRequest(_context.Object);
            handler.Verify();
        }
    }
}
