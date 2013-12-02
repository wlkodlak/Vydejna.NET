using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Tests.HttpTests
{
    [TestClass, NUnit.Framework.TestFixture]
    public class ParametrizedUrlTest
    {
        [TestMethod, NUnit.Framework.Test]
        public void GenerateWithoutParameters()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/articles/5847-restclient");
            Assert.AreEqual("http://rest.wilczak.net/web/articles/5847-restclient",
                url.CompleteUrl(Enumerable.Empty<RequestParameter>()));
        }

        [TestMethod, NUnit.Framework.Test]
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

        [TestMethod, NUnit.Framework.Test]
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

        [TestMethod, NUnit.Framework.Test]
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

        [TestMethod, NUnit.Framework.Test]
        public void GenerateUrlEndingWithSlash()
        {
            var url = new ParametrizedUrl("http://rest.wilczak.net/web/articles/");
            Assert.AreEqual("http://rest.wilczak.net/web/articles/",
                url.CompleteUrl(Enumerable.Empty<RequestParameter>()));
        }

        [TestMethod, NUnit.Framework.Test]
        public void UrlForMatching()
        {
            TestUrlForMatching(new[] { "articles", "5847" }, ParametrizedUrl.UrlForMatching("http://localhost/articles/5847?sort=date"));
            TestUrlForMatching(new[] { "resources", "1111", "" }, ParametrizedUrl.UrlForMatching("http://localhost/resources/1111/"));
        }
        private static void TestUrlForMatching(string[] expected, IList<string> actual)
        {
            var left = string.Join("/", expected);
            if (actual == null)
                Assert.Fail("Nothing was created for {0}", left);
            var right = string.Join("/", actual.ToArray());
            Assert.AreEqual(left, right);
        }

        [TestMethod, NUnit.Framework.Test]
        public void MatchOnlyFixed()
        {
            var pattern = new ParametrizedUrl("/articles/1547");
            MatchTest(pattern, "/articles/1547", "33");
            MatchTest(pattern, "/resource/1111", null);
            MatchTest(pattern, "/articles/1547/", "33");
            MatchTest(pattern, "/articles/", null);
            MatchTest(pattern, "/articles/1547/ordered", null);
        }

        [TestMethod, NUnit.Framework.Test]
        public void MatchVariables()
        {
            var pattern = new ParametrizedUrl("/articles/{id}");
            MatchTest(pattern, "/articles/1547", "31", "id", "1547");
            MatchTest(pattern, "/resource/1111", null);
            MatchTest(pattern, "/articles/1547/", "31", "id", "1547");
            MatchTest(pattern, "/articles/", "30", "id", "");
            MatchTest(pattern, "/articles/1547/ordered", null);
            var pattern2 = new ParametrizedUrl("/articles/{id}/{action}");
            MatchTest(pattern2, "/articles/", "300", "id", "", "action", "");
            MatchTest(pattern2, "/articles/1547", "310", "id", "1547", "action", "");
            MatchTest(pattern2, "/articles/1547/ordered", "311", "id", "1547", "action", "ordered");
        }

        [TestMethod, NUnit.Framework.Test]
        public void MatchComposites()
        {
            var pattern = new ParametrizedUrl("/articles/{id}-{name}");
            MatchTest(pattern, "/articles/1547-test", "32", "id", "1547", "name", "test");
            MatchTest(pattern, "/resource/1111", null);
            MatchTest(pattern, "/articles/1547-test/", "32", "id", "1547", "name", "test");
            MatchTest(pattern, "/articles/", null);
            MatchTest(pattern, "/articles/1547-ordered/action", null);
        }

        private void MatchTest(ParametrizedUrl pattern, string url, string score, params string[] variables)
        {
            var urlParts = ParametrizedUrl.UrlForMatching("http://localhost" + url);
            var match = pattern.Match(urlParts);
            Assert.IsNotNull(match, "Match must never be null ({0} for {1})", pattern, url);
            if (score == null)
                Assert.IsFalse(match.Success, "Match not expected ({0} for {1})", pattern, url);
            else
            {
                Assert.AreEqual(MatchScoreInt(score), match.Score, "Score ({0} for {1})", pattern, url);
                var parameters = match.ToLookup(p => p.Name, p => p.Value);
                for (int i = 0; i < variables.Length; i += 2)
                {
                    var values = parameters[variables[i]].ToList();
                    Assert.IsTrue(values.Count == 1, "Parameter {2} not found ({0} for {1})", pattern, url, variables[i]);
                    Assert.AreEqual(variables[i + 1], values[0], "Parameter {2} ({0} for {1})", pattern, url, variables[i]);
                }
                Assert.AreEqual(variables.Length / 2, match.Count, "Parameters count ({0} for {1})", pattern, url);
            }
        }

        private static int MatchScoreInt(string s)
        {
            int result = 0;
            for (int i = 0; i < s.Length; i++)
            {
                switch (s[i])
                {
                    case '0':
                        result = ParametrizedUrlMatch.GetScoreUpdate(result, 0);
                        break;
                    case '1':
                        result = ParametrizedUrlMatch.GetScoreUpdate(result, 1);
                        break;
                    case '2':
                        result = ParametrizedUrlMatch.GetScoreUpdate(result, 2);
                        break;
                    case '3':
                        result = ParametrizedUrlMatch.GetScoreUpdate(result, 3);
                        break;
                    default:
                        throw new Exception("Invalid score " + s);
                }
            }
            return result;
        }


        [TestMethod, NUnit.Framework.Test]
        public void ParseQueryString_Basic()
        {
            var parsed = ParametrizedUrl.ParseQueryString("http://localhost/action?param1=58&param2=true&p=hello_world").ToList();
            Assert.AreEqual(3, parsed.Count, "Count");
            var expected = new[] { "param1", "58", "param2", "true", "p", "hello_world" };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected[i * 2], parsed[i].Name, "Name {0}", i);
                Assert.AreEqual(expected[i * 2 + 1], parsed[i].Value, "Value {0}", i);
            }
        }

        [TestMethod, NUnit.Framework.Test]
        public void ParseQueryString_SpecialCharacters()
        {
            var parsed = ParametrizedUrl.ParseQueryString("http://localhost/action?spaces=hello+world&empty=&percents=%23+%2F=%3D%3F").ToList();
            Assert.AreEqual(3, parsed.Count, "Count");
            var expected = new[] { "spaces", "hello world", "empty", "", "percents", "# /==?" };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected[i * 2], parsed[i].Name, "Name {0}", i);
                Assert.AreEqual(expected[i * 2 + 1], parsed[i].Value, "Value {0}", i);
            }
        }
    }
}
