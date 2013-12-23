using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Gui;
using Vydejna.Gui.Common;

namespace Vydejna.Tests
{
    [TestClass]
    public class SerializingOnlineQueryTest
    {
        private class QueryState
        {
            private bool _immediate;
            private TaskCompletionSource<bool> _taskSource;
            private IList<string> _reports;

            public int Index;
            public string Value;
            public bool Reported;
            public Task RunTask;

            private QueryState()
            {
                _taskSource = new TaskCompletionSource<bool>();
            }

            public void SignalResult()
            {
                Value = "CNT" + Index;
                _taskSource.SetResult(true);
            }

            public void RunIn(SerializingOnlineQuery<QueryState> query)
            {
                RunTask = query.Run(this);
            }

            public static bool WaitFor(int timeout, params QueryState[] states)
            {
                var tasks = states.Where(s => s != null && s.RunTask != null).Select(s => s.RunTask).ToArray();
                return Task.WaitAll(tasks, timeout);
            }

            private static bool ImmediateFunc(QueryState state)
            {
                state.Value = "IMM" + state.Index;
                return state._immediate;
            }

            private static Task OnlineFunc(QueryState state)
            {
                if (state._immediate)
                    throw new InvalidOperationException("Immediate result cannot execute online request");
                return state._taskSource.Task;
            }

            private static void ReportFunc(QueryState state)
            {
                state.Reported = true;
                if (state._reports != null)
                    state._reports.Add(state.Value);
            }

            public static SerializingOnlineQuery<QueryState> CreateQuery()
            {
                return new SerializingOnlineQuery<QueryState>(QueryState.ImmediateFunc, QueryState.OnlineFunc, QueryState.ReportFunc, true);
            }

            public static QueryState Immediate(int index, IList<string> reports = null)
            {
                return new QueryState { _immediate = true, Index = index, _reports = reports };
            }

            public static QueryState Online(int index, IList<string> reports = null)
            {
                return new QueryState { Index = index, _reports = reports };
            }
        }

        [TestMethod]
        public void ImmediateResult()
        {
            var query = QueryState.CreateQuery();
            var state = QueryState.Immediate(1);
            state.RunIn(query);
            Assert.AreEqual("IMM1", state.Value, "Value");
            Assert.IsTrue(state.Reported, "Reported");
        }

        [TestMethod]
        public void OnlineResult()
        {
            var query = QueryState.CreateQuery();
            var state = QueryState.Online(1);
            state.RunIn(query);
            Assert.IsFalse(state.Reported, "Not supposed to report before online results come");
            state.SignalResult();
            QueryState.WaitFor(1000, state);
            Assert.AreEqual("CNT1", state.Value, "Value");
            Assert.IsTrue(state.Reported, "Reported");
        }

        [TestMethod]
        public void CancelByImmediate()
        {
            var reports = new List<string>();
            var query = QueryState.CreateQuery();
            var states = new QueryState[] { QueryState.Online(1, reports), QueryState.Immediate(2, reports) };
            states[0].RunIn(query);
            states[1].RunIn(query);
            states[0].SignalResult();
            Assert.IsTrue(QueryState.WaitFor(1000, states), "Waiting timed out");
            AssertEqualCollections(new string[] { "IMM2" }, reports);
        }

        [TestMethod]
        public void CancelWaitByImmediate()
        {
            var reports = new List<string>();
            var query = QueryState.CreateQuery();
            var states = new QueryState[] { QueryState.Online(1, reports), QueryState.Online(2, reports), QueryState.Immediate(3, reports) };
            states[0].RunIn(query);
            states[1].RunIn(query);
            states[2].RunIn(query);
            states[0].SignalResult();
            Assert.IsTrue(QueryState.WaitFor(1000, states), "Waiting timed out");
            AssertEqualCollections(new string[] { "IMM3" }, reports);
        }

        [TestMethod]
        public void IgnoreBecauseOfWaiting()
        {
            var reports = new List<string>();
            var query = QueryState.CreateQuery();
            var states = new QueryState[] { QueryState.Online(1, reports), QueryState.Online(2, reports) };
            states[0].RunIn(query);
            states[1].RunIn(query);
            states[0].SignalResult();
            states[1].SignalResult();
            Assert.IsTrue(QueryState.WaitFor(1000, states), "Waiting timed out");
            AssertEqualCollections(new string[] { "CNT2" }, reports);
        }

        [TestMethod]
        public void OnlineAfterImmediate()
        {
            var reports = new List<string>();
            var query = QueryState.CreateQuery();
            var states = new QueryState[] { QueryState.Online(1, reports), QueryState.Immediate(2, reports), QueryState.Online(3, reports) };
            states[0].RunIn(query);
            states[1].RunIn(query);
            states[2].RunIn(query);
            states[0].SignalResult();
            states[2].SignalResult();
            Assert.IsTrue(QueryState.WaitFor(1000, states), "Waiting timed out");
            AssertEqualCollections(new string[] { "IMM2", "CNT3" }, reports);
        }

        private void AssertEqualCollections(IList<string> expected, IList<string> actual)
        {
            if (expected.Count != actual.Count)
            {
                Assert.Fail(
                    "Collection count different, expected {0}, was {1}.\r\n" +
                    "Expected content: {2}\r\n" +
                    "Actual content: {3}",
                    expected.Count, actual.Count,
                    string.Join(", ", expected), string.Join(", ", actual));
            }
            else
            {
                for (int i = 0; i < expected.Count; i++)
                {
                    if (!object.Equals(expected[i], actual[i]))
                        Assert.Fail(
                            "Collections are different at index {0}\r\n" +
                            "Expected content: {1}\r\n" +
                            "Actual content: {2}",
                            i, string.Join(", ", expected), string.Join(", ", actual));
                }
            }
        }
    }
}
