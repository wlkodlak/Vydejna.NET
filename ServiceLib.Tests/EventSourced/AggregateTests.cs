﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace ServiceLib.Tests.EventSourced
{
    [TestClass]
    public class AggregateTests
    {
        [TestMethod]
        public void CreateNew()
        {
            var agg = new TestAggregate();
            var iagg = (IEventSourcedAggregate)agg;
            Assert.AreNotEqual(Guid.Empty, agg.Id, "Initial Id");
            Assert.AreEqual(0, iagg.OriginalVersion, "Original version");
            var changes = iagg.GetChanges();
            Assert.IsNotNull(changes, "Changes");
            Assert.AreEqual(1, changes.Count, "Changes");
            Assert.IsInstanceOfType(changes[0], typeof(AggregateCreated), "Changes[0]");
        }

        [TestMethod]
        public void UpdatingExisting()
        {
            var agg = new TestAggregate();
            var iagg = (IEventSourcedAggregate)agg;
            var guid = Guid.NewGuid();
            iagg.LoadFromEvents(new object[] { new AggregateCreated { Id = guid } });
            iagg.CommitChanges(1);
            
            agg.Increment();
            
            Assert.AreEqual(guid.ToId(), agg.Id, "Id");
            Assert.AreEqual(1, iagg.OriginalVersion, "Original version");
            var changes = iagg.GetChanges();
            Assert.IsNotNull(changes, "Changes");
            Assert.AreEqual(1, changes.Count, "Changes");
            Assert.IsInstanceOfType(changes[0], typeof(AggregateIncrement), "Changes[0]");
        }

        [TestMethod]
        public void SavingChanges()
        {
            var agg = new TestAggregate();
            var iagg = (IEventSourcedAggregate)agg;
            var guid = Guid.NewGuid();
            iagg.LoadFromEvents(new object[] { new AggregateCreated { Id = guid } });
            iagg.CommitChanges(1);
            agg.Increment();

            iagg.CommitChanges(2);

            Assert.AreEqual(guid.ToId(), agg.Id, "Id");
            Assert.AreEqual(2, iagg.OriginalVersion, "Original version");
            var changes = iagg.GetChanges();
            Assert.IsNotNull(changes, "Changes");
            Assert.AreEqual(0, changes.Count, "Changes");
        }

        private class TestAggregate : EventSourcedGuidAggregate
        {
            public int State = 0;

            public TestAggregate()
            {
                RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
                Apply(new AggregateCreated { Id = Guid.NewGuid() });
            }
            public void Increment()
            {
                Apply(new AggregateIncrement());
            }
            protected void Apply(AggregateCreated evnt)
            {
                RecordChange(evnt);
                SetGuid(evnt.Id);
            }
            protected void Apply(AggregateIncrement evnt)
            {
                RecordChange(evnt);
                State++;
            }
        }

        private class AggregateCreated { public Guid Id;}
        private class AggregateIncrement { }
    }
}
