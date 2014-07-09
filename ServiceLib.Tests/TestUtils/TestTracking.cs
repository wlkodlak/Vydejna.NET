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

    public class TestTrackSource : IEventProcessTrackSource
    {
        public bool Committed;
        public EventStoreToken LastToken = EventStoreToken.Initial;
        public int TrackedEvents = 0;
        public string TrackingId;

        string IEventProcessTrackSource.TrackingId
        {
            get { return TrackingId; }
        }

        public void AddEvent(EventStoreToken token)
        {
            TrackedEvents++;
            if (EventStoreToken.Compare(token, LastToken) >= 0)
                LastToken = token;
        }

        public void CommitToTracker()
        {
            Committed = true;
        }
    }

    public class TestTracking : IEventProcessTrackCoordinator
    {
        public IEventProcessTrackSource CreateTracker()
        {
            return new TestTrackSource();
        }

        public IEventProcessTrackItem FindTracker(string trackingId)
        {
            throw new NotSupportedException();
        }

        public IEventProcessTrackTarget RegisterHandler(string handlerName)
        {
            throw new NotSupportedException();
        }
    }

}
