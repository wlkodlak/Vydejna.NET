using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventHandlers
{
    public abstract class PureProjectionTestBase
    {
        protected class TestState
        {
            private EventStoreToken _token;
            private string _output;

            public TestState(EventStoreToken token, string output)
            {
                _token = token;
                _output = output;
            }

            public TestState(string token, string output)
            {
                _token = new EventStoreToken(token);
                _output = output;
            }

            public EventStoreToken Token { get { return _token; } }
            public string Output { get { return _output; } }

            public static TestState Initial { get { return new TestState(EventStoreToken.Initial, null); } }
            public TestState Add(string prefix, string data)
            {
                string newOutput;
                if (_output == null)
                    newOutput = string.Concat(prefix, data);
                else
                    newOutput = string.Concat(_output, " ", prefix, data);
                return new TestState(_token, newOutput);
            }
            public TestState ApplyToken(EventStoreToken token)
            {
                return new TestState(token, _output);
            }
            public TestState ApplyToken(string token)
            {
                return new TestState(token, _output);
            }
            public override string ToString()
            {
                return string.Concat("Token: ", _token.ToString(), "\r\n", _output);
            }
            public override int GetHashCode()
            {
                return _output == null ? 0 : _output.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                var oth = obj as TestState;
                return oth != null && _token.Equals(oth._token) && string.Equals(_output, oth._output, StringComparison.Ordinal);
            }
        }

        protected class TestEvent { public string Partition, Data; }
        protected class TestEvent1 : TestEvent { }
        protected class TestEvent2 : TestEvent { }
        protected class TestEvent3 : TestEvent { }
        protected class TestEvent4 : TestEvent { }

        protected class TestHandler
            : IPureProjectionHandler<TestState, TestEvent1>
            , IPureProjectionHandler<TestState, TestEvent2>
            , IPureProjectionHandler<TestState, TestEvent3>
            , IPureProjectionStateToken<TestState>
            , IPureProjectionVersionControl
        {

            public string Partition(TestEvent1 evnt)
            {
                return evnt.Partition;
            }

            public TestState ApplyEvent(TestState state, TestEvent1 evnt, EventStoreToken token)
            {
                return state.Add("E1:", evnt.Data ?? "");
            }

            public string Partition(TestEvent2 evnt)
            {
                return evnt.Partition;
            }

            public TestState ApplyEvent(TestState state, TestEvent2 evnt, EventStoreToken token)
            {
                return state.Add("E2:", evnt.Data ?? "");
            }

            public string Partition(TestEvent3 evnt)
            {
                return evnt.Partition;
            }

            public TestState ApplyEvent(TestState state, TestEvent3 evnt, EventStoreToken token)
            {
                return state.Add("E3:", evnt.Data ?? "");
            }

            public TestState SetTokenInState(TestState state, EventStoreToken token)
            {
                return state.ApplyToken(token);
            }

            public EventStoreToken GetTokenFromState(TestState state)
            {
                return state.Token;
            }

            public string GetVersion()
            {
                return "1.0";
            }

            public bool NeedsRebuild(string storedVersion)
            {
                return string.IsNullOrEmpty(storedVersion) || storedVersion != "1.0";
            }
        }

        protected class TestSerializer : IPureProjectionSerializer<TestState>
        {
            public string Serialize(TestState state)
            {
                return string.Concat(state.Token.ToString(), "\r\n", state.Output);
            }

            public TestState Deserialize(string serializedState)
            {
                if (string.IsNullOrEmpty(serializedState))
                    return InitialState();
                else
                {
                    var parts = serializedState.Split(new[] { "\r\n" }, 2,  StringSplitOptions.None);
                    return new TestState(new EventStoreToken(parts[0]), parts[1]);
                }
            }

            public TestState InitialState()
            {
                return TestState.Initial;
            }
        }
    }
}
