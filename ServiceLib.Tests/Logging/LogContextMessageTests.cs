using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ServiceLib.Tests.Logging
{
    [TestClass]
    public class LogContextMessageTests
    {
        private TestTraceListener _testListener;
        private TraceSource _testSource;
        private LogContextMessage _message;

        private class TestTraceListener : TraceListener
        {
            private StringBuilder _text = new StringBuilder();
            
            public TraceEventType EventType;
            public int EventId;
            public string Source;
            public object Message;

            public override void Write(string message)
            {
                _text.Append(message);
            }

            public override void WriteLine(string message)
            {
                _text.AppendLine(message);
            }

            public string GetText()
            {
                return _text.ToString();
            }

            public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
            {
                base.TraceData(eventCache, source, eventType, id, data);
                EventType = eventType;
                EventId = id;
                Source = source;
                Message = data;
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            _testListener = new TestTraceListener();
            _testSource = new TraceSource(Guid.NewGuid().ToString());
            _testSource.Listeners.Clear();
            _testSource.Listeners.Add(_testListener);
            _testSource.Switch = new SourceSwitch("Verbose", "Verbose");

            _message = new LogContextMessage(TraceEventType.Information, 58, "Event {EventId} arrived.");
            _message.SetProperty("EventId", false, "384922");
            _message.SetProperty("Body", true, "<root>\r\n  <element id=\"abc\" />\r\n</root>");
        }

        [TestMethod]
        public void LogMessageWithProperties()
        {
            _message.Log(_testSource);
            Assert.AreEqual(TraceEventType.Information, _testListener.EventType);
            Assert.AreEqual(58, _testListener.EventId, "EventId");
            Assert.AreSame(_message, _testListener.Message, "Message");
        }

        [TestMethod]
        public void EnumerateProperties()
        {
            Assert.AreEqual("Body, EventId", string.Join(", ", _message.Select(p => p.Name).OrderBy(n => n)));
            Assert.AreEqual("384922", _message.Where(p => p.Name == "EventId").Select(p => p.Value).FirstOrDefault());
        }

        [TestMethod]
        public void GetPropertyValue()
        {
            Assert.AreEqual("384922", _message.GetProperty("EventId"));
            Assert.AreEqual("<root>\r\n  <element id=\"abc\" />\r\n</root>", _message.GetProperty("Body"));
        }

        [TestMethod]
        public void MessageToString()
        {
            var expected = "Event 384922 arrived. EventId=384922\r\nBody:\r\n<root>\r\n  <element id=\"abc\" />\r\n</root>\r\n";
            Assert.AreEqual(expected, _message.ToString());
        }
    }
}
