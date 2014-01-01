using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class ParametrizedUrlTest_CompleteUrl
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

    [TestClass]
    public class ParametrizedUrlTest_Matching
    {
        [TestMethod]
        public void UrlForMatching()
        {
            TestUrlForMatching(new[] { "articles", "5847" }, ParametrizedUrl.UrlForMatching("http://localhost/articles/5847?sort=date"));
            TestUrlForMatching(new[] { "resources", "1111", "" }, ParametrizedUrl.UrlForMatching("http://localhost/resources/1111/"));
        }
        private static void TestUrlForMatching(string[] expected, ParametrizedUrlParts actual)
        {
            var expectedParts = new ParametrizedUrlParts(expected);
            if (actual == null)
                Assert.Fail("Nothing was created for {0}", expectedParts);
            Assert.AreEqual(expectedParts, actual);
        }

        [TestMethod]
        public void MatchOnlyFixed()
        {
            var pattern = new ParametrizedUrl("/articles/1547");
            MatchTest(pattern, "/articles/1547", "33");
            MatchTest(pattern, "/resource/1111", null);
            MatchTest(pattern, "/articles/1547/", "33");
            MatchTest(pattern, "/articles/", null);
            MatchTest(pattern, "/articles/1547/ordered", null);
        }

        [TestMethod]
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

        [TestMethod]
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
    }

    [TestClass]
    public class ParametrizedUrlTest_QueryString
    {
        [TestMethod]
        public void ParseQueryString_Empty()
        {
            var parsed = ParametrizedUrl.ParseQueryString("http://localhost/action?").ToList();
            Assert.AreEqual(0, parsed.Count, "Count");
        }

        [TestMethod]
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

        [TestMethod]
        public void ParseQueryString_WithoutValue()
        {
            var parsed = ParametrizedUrl.ParseQueryString("http://localhost/action?name").ToList();
            Assert.AreEqual(1, parsed.Count, "Count");
            var expected = new[] { "name", "name" };
            for (int i = 0; i < 1; i++)
            {
                Assert.AreEqual(expected[i * 2], parsed[i].Name, "Name {0}", i);
                Assert.AreEqual(expected[i * 2 + 1], parsed[i].Value, "Value {0}", i);
            }
        }

        [TestMethod]
        public void ParseQueryString_WithoutName()
        {
            var parsed = ParametrizedUrl.ParseQueryString("http://localhost/action?=name").ToList();
            Assert.AreEqual(0, parsed.Count, "Count");
        }

        [TestMethod]
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
    
    [TestClass]
    public class ParametrizedUrlTest_Parts
    {
        [TestMethod]
        public void UrlParts_Elements()
        {
            var parts = new ParametrizedUrlParts("articles", "5845");
            Assert.AreEqual(2, parts.Count, "Count");
            Assert.AreEqual("articles", parts[0], "[0]");
            Assert.AreEqual("5845", parts[1], "[1]");
        }

        [TestMethod]
        public void UrlParts_Comparing()
        {
            var a = new ParametrizedUrlParts("article", "1111");
            var b = new ParametrizedUrlParts("article", "1111");
            var c = new ParametrizedUrlParts("article", "3923");
            var d = new ParametrizedUrlParts("category", "1111");
            var e = new ParametrizedUrlParts("article");
            var f = new ParametrizedUrlParts("category");
            AssertPartsCompare(a, b, 0);
            AssertPartsCompare(a, c, -2);
            AssertPartsCompare(a, d, -2);
            AssertPartsCompare(a, e, 1);
            AssertPartsCompare(a, f, -2);
            AssertPartsCompare(f, d, -1);
            AssertPartsCompare(f, e, 2);
        }

        private void AssertPartsCompare(IComparable<ParametrizedUrlParts> a, ParametrizedUrlParts b, int expect)
        {
            var comparison = a.CompareTo(b);
            var op = expect == 0 ? "==" : expect < 0 ? "<" : ">";
            Assert.AreEqual(expect, comparison, "{0} {2} {1}", a, b, op);
            if (expect == 0)
                Assert.AreEqual(a, b);
            else
                Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void GetPrefix()
        {
            GetPrefixTest("/articles/55833", "articles", "55833");
            GetPrefixTest("/articles/", "articles");
            GetPrefixTest("/articles/{id}", "articles");
            GetPrefixTest("/articles/{id}-{name}", "articles");
            GetPrefixTest("/articles/{id}/comments", "articles");
            GetPrefixTest("/category/all", "category", "all");
            GetPrefixTest("/");
        }

        private void GetPrefixTest(string url, params string[] parts)
        {
            var expected = new ParametrizedUrlParts(parts);
            var actual = new ParametrizedUrl(url).Prefix;
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void IsPrefixOf()
        {
            IsPrefixOfTest(new ParametrizedUrlParts(), new ParametrizedUrlParts("articles", "5845"), true);
            IsPrefixOfTest(new ParametrizedUrlParts("articles"), new ParametrizedUrlParts("articles", "5845"), true);
            IsPrefixOfTest(new ParametrizedUrlParts("articles", "5845"), new ParametrizedUrlParts("articles", "5845"), true);
            IsPrefixOfTest(new ParametrizedUrlParts("category"), new ParametrizedUrlParts("articles", "5845"), false);
            IsPrefixOfTest(new ParametrizedUrlParts("article"), new ParametrizedUrlParts("articles"), false);
            IsPrefixOfTest(new ParametrizedUrlParts("articles", "5845"), new ParametrizedUrlParts("articles"), false);
        }

        private static void IsPrefixOfTest(ParametrizedUrlParts a, ParametrizedUrlParts b, bool expected)
        {
            var isPrefix = a.IsPrefixOf(b);
            Assert.AreEqual(expected, isPrefix, 
                "Should {2}be prefix:\r\n{0}\r\n{1}", 
                a, b, expected ? "" : "not ");
        }

    }
}
