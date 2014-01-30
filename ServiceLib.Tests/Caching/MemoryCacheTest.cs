using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ServiceLib.Tests.Caching
{
    [TestClass]
    public class MemoryCacheTest
    {
        private MemoryCache<string> _cache;
        private List<LoadResult> _loadResults;
        private int _loadResultIdx;

        [TestInitialize]
        public void Initialize()
        {
            _cache = new MemoryCache<string>();
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
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            Tick();
            StartGetting("01", l => { });
            ExpectNoLoading();
            ExpectValue("01", 2, "Value");
        }

        [TestMethod]
        public void NonfreshValueUsesOptionalLoad()
        {
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(3, "Value2"));
            ExpectLoading("01", 2, "Value");
            ExpectValue("01", 3, "Value2");
        }

        [TestMethod]
        public void NonfreshLoadCanLoadNewValue()
        {
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.SetLoadedValue(3, "Value2"));
            ExpectValue("01", 3, "Value2");
        }

        [TestMethod]
        public void NonfreshLoadCanKeepValue()
        {
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
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
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 4; i++)
                Tick();
            StartGetting("01", l => l.Expires(1, 8).ValueIsStillValid());
            ExpectValue("01", 2, "Value");
            Tick();
            StartGetting("01", l => l.LoadingFailed(new InvalidOperationException()));
            ExpectValue("01", 2, "Value");
        }

        [TestMethod]
        public void OnlyOneRefreshIsPerformedAtOnceForSameKey()
        {
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
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
            StartGetting("01", l => l.Expires(1, 8).SetLoadedValue(2, "Value"));
            ExpectValue("01", 2, "Value");
            for (int i = 0; i < 10; i++)
                Tick();
            StartGetting("01", l => { });
            ExpectLoading("01", -1, null);
        }


        private void StartGetting(string key, Action<IMemoryCacheLoad<string>> loader)
        {
            _cache.Get(key,
                (v, s) => _loadResults.Add(LoadResult.Loaded(key, v, s)),
                e => _loadResults.Add(LoadResult.Error(key, e)),
                loader);
        }

        private void ExpectFullLoading(string key)
        {
            Assert.IsTrue(_loadResults.Count > _loadResultIdx, "Loading is not ready");
            var loading = _loadResults[_loadResultIdx];
            Assert.AreEqual(LoadResult.Loaded(key, -1, null), loading);
        }

        private void ExpectValue(string key, int version, string value)
        {
            Assert.IsTrue(_loadResults.Count > _loadResultIdx, "Loading is not ready");
            var loading = _loadResults[_loadResultIdx];
            Assert.AreEqual(LoadResult.Loaded(key, version, value), loading);
        }

        private void ExpectError<T>(string key)
        {
            Assert.IsTrue(_loadResults.Count > _loadResultIdx, "Loading is not ready");
            var loading = _loadResults[_loadResultIdx];
            Assert.AreEqual(LoadResult.Error(key, typeof(T)), loading);
        }

        private void Tick()
        {
            _cache.EvictOldEntries(1);
        }

        private void ExpectLoading(string key, int version, string value)
        {
            throw new NotImplementedException();
        }

        private void ExpectNoLoading()
        {
            throw new NotImplementedException();
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

    }
}
