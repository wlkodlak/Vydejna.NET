using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ServiceLib.Tests.TestUtils
{
    public class TestTrackTarget : IEventProcessTrackTarget
    {
        public EventStoreToken LastToken;

        public string HandlerName
        {
            get { return "TestHandler"; }
        }

        public void ReportProgress(EventStoreToken token)
        {
            LastToken = token;
        }
    }

}
