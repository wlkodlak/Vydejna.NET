using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass]
    public class ParametrizedUrlTest
    {
        [TestMethod]
        public void GenerateWithoutParameters()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/articles/5847-restclient");
            Assert.AreEqual("http://rest.wilczak.net/web/articles/5847-restclient",
                url.CompleteUrl(Enumerable.Empty<RequestParameter>()));
        }

        [TestMethod]
        public void GenerateWithQueryParameters()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/articles/5847-restclient");
            var parameters = new List<RequestParameter>()
            {
                new RequestParameter(RequestParameterType.QueryString, "param1", "value1"),
                new RequestParameter(RequestParameterType.QueryString, "param2", "value2"),
                new RequestParameter(RequestParameterType.QueryString, "param3", "value3")
            };
            Assert.AreEqual("http://rest.wilczak.net/web/articles/5847-restclient?param1=value1&param2=value2&param3=value3", 
                url.CompleteUrl(parameters));
        }

        [TestMethod]
        public void GenerateWithPathParameters()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/{controller}/{id}-{article_name}");
            var parameters = new List<RequestParameter>()
            {
                new RequestParameter(RequestParameterType.Path, "controller", "articles"),
                new RequestParameter(RequestParameterType.Path, "id", "5847"),
                new RequestParameter(RequestParameterType.Path, "article_name", "restclient")
            };
            Assert.AreEqual("http://rest.wilczak.net/web/articles/5847-restclient",
                url.CompleteUrl(parameters));
        }

        [TestMethod]
        public void GenerateComplex()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/{controller}/{id}-{article_name}?param1=value1");
            var parameters = new List<RequestParameter>()
            {
                new RequestParameter(RequestParameterType.Path, "controller", "articles"),
                new RequestParameter(RequestParameterType.Path, "id", "5847"),
                new RequestParameter(RequestParameterType.Path, "article_name", "restclient"),
                new RequestParameter(RequestParameterType.QueryString, "param2", "value2"),
                new RequestParameter(RequestParameterType.QueryString, "param3", "value3")
            };
            Assert.AreEqual("http://rest.wilczak.net/web/articles/5847-restclient?param1=value1&param2=value2&param3=value3",
                url.CompleteUrl(parameters));
        }

        [TestMethod]
        public void GenerateUrlEndingWithSlash()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/articles/");
            Assert.AreEqual("http://rest.wilczak.net/web/articles/", 
                url.CompleteUrl(Enumerable.Empty<RequestParameter>()));

        }
    }
}
