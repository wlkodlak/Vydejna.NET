using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using ServiceLib.Tests.TestUtils;

namespace ServiceLib.Tests.Caching
{
    [TestClass]
    public class MemoryCacheTest
    {
        private MemoryCache<string> _cache;
        private List<LoadResult> _loadResults;
        private List<Loading> _loadings;
        private int _loadResultIdx;
        private int _loadingIdx;
        private TestExecutor _executor;
        private VirtualTime _time;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2014, 1, 14, 18, 20, 14));
            _cache = new MemoryCache<string>(_executor, _time);
            _cache.SetupExpiration(200, 60000, 1000);
            _loadingIdx = _loadResultIdx = 0;
            _loadResults = new List<LoadResult>();
            _loadings = new List<Loading>();
        }

        [TestMethod]
        public void ForEmptyCacheGetCausesLoading()
        {
            StartGetting("01", l => { });
            ExpectLoading("01", -1, null);
        }

        [TestMethod]
        public void JustLoadedValueIsSentUsingCallback()
        {
            StartGetting("01", l => l.SetLoadedValue(1, "Hello"));
            ExpectValue("01", 1, "Hello");
        }

        [TestMethod]
        public void OnlyOneLoadIsStarted()
        {
            StartGetting("01", l => { });
            StartGetting("01", l => { });
            ExpectLoading("01", -1, null);
            ExpectNoLoading();
        }

        [TestMethod]
        public void AllCallersGetResults()
        {
            StartGetting("01", l => l.SetLoadedValue(1, "Hello"));
            StartGetting("01", l => l.SetLoadedValue(1, "Hello"));
            ExpectValue("01", 1, "Hello");
            ExpectValue("01", 1, "Hello");
        }

        [TestMethod]
        public void FailedLoadSentToAll()
        {
            StartGetting("01", l => l.LoadingFailed(new FormatException()));
            StartGetting("01", l => l.LoadingFailed(new FormatException()));
            ExpectError<FormatException>("01");
            ExpectError<FormatException>("01");
        }

        [TestMethod]
        public void FreshValueIsReturnedImmediatelly()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            Tick();
            StartGetting("01", l => { });
            ExpectNoLoading();
            ExpectValue("01", 2, "Value");
        }

        [TestMethod]
        public void NonfreshValueUsesOptionalLoad()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(3, "Value2"));
            ExpectLoading("01", 2, "Value");
            ExpectValue("01", 3, "Value2");
        }

        [TestMethod]
        public void NonfreshLoadCanLoadNewValue()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.SetLoadedValue(3, "Value2"));
            ExpectValue("01", 3, "Value2");
        }

        [TestMethod]
        public void NonfreshLoadCanKeepValue()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.ValueIsStillValid());
            ExpectLoading("01", 2, "Value");
            ExpectValue("01", 2, "Value");
        }

        [TestMethod]
        public void RefreshedValueStartsNewUncheckedPeriod()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.Expires(200, 60000).ValueIsStillValid());
            ExpectValue("01", 2, "Value");
            Tick();
            StartGetting("01", l => l.LoadingFailed(new InvalidOperationException()));
            ExpectValue("01", 2, "Value");
        }

        [TestMethod]
        public void OnlyOneRefreshIsPerformedAtOnceForSameKey()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => { });
            StartGetting("01", l => { });
            ExpectLoading("01", 2, "Value");
            ExpectNoLoading();
        }

        [TestMethod]
        public void DifferentKeysLoadSimultaneously()
        {
            StartGetting("01", l => { });
            StartGetting("02", l => { });
            ExpectLoading("01", -1, null);
            ExpectLoading("02", -1, null);
        }

        [TestMethod]
        public void ExpiredKeyCausesFullLoad()
        {
            StartGetting("01", l => l.Expires(200, 5000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 60; i++)
                Tick();
            StartGetting("01", l => { });
            ExpectLoading("01", -1, null);
        }

        [TestMethod]
        public void EvictedKeyCausesFullLoad()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            Evict("01", false);
            StartGetting("01", l => { });
            ExpectLoading("01", -1, null);
        }

        [TestMethod]
        public void InvalidatedKeyCausesRefresh()
        {
            StartGetting("01", l => l.Expires(200, 60000).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            Evict("01", true);
            StartGetting("01", l => { });
            ExpectLoading("01", 2, "Value");
        }


        private void StartGetting(string key, Action<IMemoryCacheLoad<string>> loader)
        {
            _cache.Get(key,
                (v, s) => _loadResults.Add(LoadResult.Loaded(key, v, s)),
                e => _loadResults.Add(LoadResult.Error(key, e)),
                l => { _loadings.Add(Loading.Check(key, l.OldVersion, l.OldValue)); loader(l); });
            _executor.Process();
        }

        private void ExpectValue(string key, int version, string value)
        {
            Assert.IsTrue(_loadResults.Count > _loadResultIdx, "Result is not ready");
            var loading = _loadResults[_loadResultIdx];
            Assert.AreEqual(LoadResult.Loaded(key, version, value), loading);
            _loadResultIdx++;
            _loadingIdx = _loadings.Count;
        }

        private void ExpectError<T>(string key)
        {
            Assert.IsTrue(_loadResults.Count > _loadResultIdx, "Result is not ready");
            var loading = _loadResults[_loadResultIdx];
            Assert.AreEqual(LoadResult.Error(key, typeof(T)), loading);
            _loadResultIdx++;
            _loadingIdx = _loadings.Count;
        }

        private void Tick()
        {
            _time.SetTime(_time.GetUtcTime().AddMilliseconds(100));
            _executor.Process();
        }

        private void Evict(string key, bool onlyInvalidate)
        {
            if (onlyInvalidate)
                _cache.Invalidate(key);
            else
                _cache.Evict(key);
            _executor.Process();
        }

        private void ExpectFullLoading(string key)
        {
            Assert.IsTrue(_loadings.Count > _loadingIdx, "No loading started");
            var loading = _loadings[_loadingIdx];
            Assert.AreEqual(Loading.Full(key), loading);
            _loadingIdx++;
        }

        private void ExpectLoading(string key, int version, string value)
        {
            Assert.IsTrue(_loadings.Count > _loadingIdx, "No loading started");
            var loading = _loadings[_loadingIdx];
            Assert.AreEqual(Loading.Check(key, version, value), loading);
            _loadingIdx++;
        }

        private void ExpectNoLoading()
        {
            Assert.IsTrue(_loadings.Count == _loadingIdx, "No loading expected");
        }



        private class LoadResult
        {
            public string Key, Value;
            public int Version;
            public Type Exception;
            private string Mode;

            public static LoadResult Loaded(string key, int version, string value) { return new LoadResult { Key = key, Mode = "Result", Version = version, Value = value }; }
            public static LoadResult Error(string key, Exception exception) { return new LoadResult { Key = key, Mode = "Error", Exception = exception.GetType() }; }
            public static LoadResult Error(string key, Type exception) { return new LoadResult { Key = key, Mode = "Error", Exception = exception }; }

            public override int GetHashCode()
            {
                if (Exception != null)
                    return Exception.GetHashCode();
                else if (Value != null)
                    return Value.GetHashCode();
                else
                    return 84729;
            }
            public override bool Equals(object obj)
            {
                return Equals(obj as LoadResult);
            }
            public bool Equals(LoadResult oth)
            {
                return oth != null && Exception == oth.Exception && Key == oth.Key && Value == oth.Value && Version == oth.Version && Mode == oth.Mode;
            }
            public override string ToString()
            {
                if (Exception != null)
                    return string.Format("{0} {1}: {2}", Mode, Key, Exception.Name);
                else
                    return string.Format("{0} {1}: {2}@{3}", Mode, Key, Version, Value);
            }
        }

        private class Loading
        {
            public string Key, Value;
            public int Version;

            public static Loading Full(string key) { return new Loading { Key = key, Version = -1, Value = null }; }
            public static Loading Check(string key, int version, string value) { return new Loading { Key = key, Version = version, Value = value }; }

            public override int GetHashCode()
            {
                return Key.GetHashCode() ^ Version;
            }
            public override bool Equals(object obj)
            {
                return Equals(obj as Loading);
            }
            public bool Equals(Loading oth)
            {
                return oth != null && Key == oth.Key && Value == oth.Value && Version == oth.Version;
            }
            public override string ToString()
            {
                if (Version == -1)
                    return string.Format("{0}: full", Key);
                else
                    return string.Format("{0}: {1}@{2}", Key, Version, Value);
            }
        }

    }
}
