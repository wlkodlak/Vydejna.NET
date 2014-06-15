using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.NaradiObecneTesty
{
    public abstract class ObecneNaradiServiceTestBase<TAggregate, TService>
        where TAggregate : class, IEventSourcedAggregate, new()
    {
        protected TestScheduler _scheduler;
        protected TService _svc;
        protected CommandResult _commandResult;
        protected TestRepository<TAggregate> _repository;
        protected Dictionary<string, int> _newEventCounts;
        protected VirtualTime _time;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2012, 1, 18, 8, 19, 21));
            _scheduler = new TestScheduler();
            _repository = new TestRepository<TAggregate>();
            InitializeCore();
            _svc = CreateService();
            _newEventCounts = null;
            _commandResult = null;
        }

        protected abstract TService CreateService();
        protected virtual void InitializeCore() { }

        protected virtual void Execute<T>(T cmd)
        {
            var task = _scheduler.Run(() => ((IProcessCommand<T>)_svc).Handle(cmd));
            _commandResult = task.Result;
            Assert.IsNotNull(_commandResult, "CommandResult");
            Assert.IsNotNull(_commandResult.Errors, "Errors");
        }

        protected void ChybaValidace(string kategorie, string polozka)
        {
            Assert.AreEqual(CommandResultStatus.InvalidCommand, _commandResult.Status, "Status");
            var chybaValidace = _commandResult.Errors.FirstOrDefault(c => c.Field == polozka) ?? _commandResult.Errors.FirstOrDefault();

            Assert.IsNotNull(chybaValidace, "Ocekavana chyba");
            Assert.AreEqual(polozka, chybaValidace.Field, "Polozka chyby validace");
            Assert.AreEqual(kategorie, chybaValidace.Category, "Kategorie chyby validace");
            Assert.AreEqual("", string.Join(", ", NewEventsTypeNames()), "Udalosti");
        }

        protected void ChybaStavu(string kategorie, string polozka)
        {
            Assert.AreEqual(CommandResultStatus.WrongState, _commandResult.Status, "Status");
            var chybaStavu = _commandResult.Errors.FirstOrDefault(c => c.Field == polozka) ?? _commandResult.Errors.FirstOrDefault();
            
            Assert.IsNotNull(chybaStavu, "Ocekavana chyba");
            Assert.AreEqual(polozka, chybaStavu.Field, "Polozka chyby stavu");
            Assert.AreEqual(kategorie, chybaStavu.Category, "Kategorie chyby stavu");
            Assert.AreEqual("", string.Join(", ", NewEventsTypeNames()), "Udalosti");
        }

        protected void Given(Guid naradiId, params object[] events)
        {
            _repository.AddEvents(naradiId.ToId(), events);
        }

        protected IList<string> NewEventsTypeNames()
        {
            return _repository.NewEvents().Select(e => e.GetType().Name).ToList();
        }

        protected T NewEventOfType<T>()
        {
            ExpectNoErrors();
            var evnt = _repository.NewEvents().OfType<T>().FirstOrDefault();
            Assert.IsNotNull(evnt, "Expected event {0}", typeof(T).Name);
            return evnt;
        }

        protected void ExpectNoErrors()
        {
            Assert.AreEqual(CommandResultStatus.Success, _commandResult.Status, "Status");
            Assert.AreEqual(0, _commandResult.Errors.Count, "Errors.Count");
        }

        protected DateTime GetUtcTime()
        {
            return _time.GetUtcTime();
        }

        protected Guid Id(string prefix)
        {
            return new Guid(prefix + "-0000-0000-0000-000000000000");
        }

        protected void Event<T>(int expectedCount = -1)
        {
            ComputeEventCounts();
            var typename = typeof(T).Name;
            int actualCount;
            if (_newEventCounts.TryGetValue(typename, out actualCount))
                _newEventCounts.Remove(typename);
            if (expectedCount >= 0)
                Assert.AreEqual(expectedCount, actualCount, "Count of event {0}", typename);
            else if (actualCount == 0)
                Assert.Fail("There is no event {0}", typename);
        }

        protected void NoMoreEvents()
        {
            ComputeEventCounts();
            var errorMessage = string.Join(", ", _newEventCounts
                .Where(p => p.Value > 0)
                .Select(p => p.Value > 1 ? string.Format("{0}x {1}", p.Value, p.Key) : p.Key));
            Assert.AreEqual("", errorMessage, "Remaining events");
        }

        private void ComputeEventCounts()
        {
            ExpectNoErrors();
            if (_newEventCounts == null)
            {
                _newEventCounts = _repository.NewEvents()
                    .Select(e => e.GetType().Name)
                    .GroupBy(s => s)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionary(g => g.Key, g => g.Count);
            }
        }
    }
}
