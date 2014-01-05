using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.EventHandlers
{
    public abstract class PureProjectionDispatcherTests_Common
    {
        protected TestHandler _handler;
        protected PureProjectionDispatcher<TestState> _dispatcher;

        [TestInitialize]
        public virtual void Initialize()
        {
            _handler = new TestHandler();
            _dispatcher = new PureProjectionDispatcher<TestState>();
            _dispatcher.Register<TestEvent1>(_handler);
            _dispatcher.Register<TestEvent2>(_handler);
            _dispatcher.Register<TestEvent3>(_handler);
        }

        protected class TestState
        {
            private EventStoreToken _token;
            private string _output;
            
            public TestState(EventStoreToken token, string output)
            {
                _token = token;
                _output = output;
            }

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
        }
    }

    [TestClass]
    public class PureProjectionDispatcherTests_Base : PureProjectionDispatcherTests_Common
    {
        [TestMethod]
        public void GetRegisteredTypes()
        {
            var expectedTypes = "TestEvent1, TestEvent2, TestEvent3";
            var actualTypes = string.Join(", ", _dispatcher.GetRegisteredTypes().Select(t => t.Name).OrderBy(n => n));
            Assert.AreEqual(expectedTypes, actualTypes);
        }

        [TestMethod]
        public void FindHandler()
        {
            var state = TestState.Initial;
            var evnt = new TestEvent2 { Partition = "A", Data = "42" };
            var impl = _dispatcher.FindHandler(evnt.GetType());
            Assert.IsNotNull(impl, "Handler null");
            Assert.AreEqual(evnt.Partition, impl.Partition(evnt), "Partition");
            var expectedState = new TestState(new EventStoreToken("1"), "E2:42");
            var actualState = impl
                .ApplyEvent(state, evnt, new EventStoreToken("1"))
                .ApplyToken(new EventStoreToken("1"));
            Assert.AreEqual(expectedState, actualState, "State");
        }


    }
}
