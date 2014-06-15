using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using Vydejna.Domain.ExterniCiselniky;
using System.Threading.Tasks;

namespace Vydejna.Domain.Tests.ExterniCiselnikyTesty
{
    [TestClass]
    public class DodaniExternihoCiselnikuTest
    {
        private TestScheduler _scheduler;
        private TestExternalEventRepository _repository;
        private ExterniCiselnikyService _service;
        private CommandResult _result;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _repository = new TestExternalEventRepository();
            _service = new ExterniCiselnikyService(_repository);
            _result = null;
        }

        protected void Execute<T>(T evnt)
        {
            var handler = (IProcessCommand<T>)_service;
            var task = _scheduler.Run(() => handler.Handle(evnt));
            if (task.Exception != null)
                throw task.Exception.InnerException.PreserveStackTrace();
            _result = task.Result;
        }

        [TestMethod]
        public void ImportVady()
        {
            Execute(new DefinovanaVadaNaradiEvent
            {
                Kod = "0",
                Deaktivovana = false,
                Nazev = "Bez vady"
            });
            var events = _repository.GetStreamEvents("vady");
            Assert.AreEqual(1, events.Count, "Count");
            Assert.IsInstanceOfType(events[0], typeof(DefinovanaVadaNaradiEvent));
            var evnt = events[0] as DefinovanaVadaNaradiEvent;
            Assert.AreEqual("0", evnt.Kod, "Kod");
            Assert.AreEqual(false, evnt.Deaktivovana, "Deaktivovana");
            Assert.AreEqual("Bez vady", evnt.Nazev, "Nazev");
        }

        [TestMethod]
        public void ImportPracoviste()
        {
            Execute(new DefinovanoPracovisteEvent
            {
                Kod = "12345110",
                Deaktivovano = true,
                Nazev = "Brouseni",
                Stredisko = "110"
            });
            var events = _repository.GetStreamEvents("pracoviste");
            Assert.AreEqual(1, events.Count, "Count");
            Assert.IsInstanceOfType(events[0], typeof(DefinovanoPracovisteEvent));
            var evnt = events[0] as DefinovanoPracovisteEvent;
            Assert.AreEqual("12345110", evnt.Kod, "Kod");
            Assert.AreEqual(true, evnt.Deaktivovano, "Deaktivovano");
            Assert.AreEqual("Brouseni", evnt.Nazev, "Nazev");
            Assert.AreEqual("110", evnt.Stredisko, "Stredisko");
        }

        [TestMethod]
        public void ImportDodavatel()
        {
            Execute(new DefinovanDodavatelEvent
            {
                Kod = "D14",
                Deaktivovan = false,
                Nazev = "Opravar, s.r.o.",
                Dic = "111-84758",
                Ico = "84758",
                Adresa = new[] { "Strojova 15", "111 00 Destna" }
            });
            var events = _repository.GetStreamEvents("dodavatele");
            Assert.AreEqual(1, events.Count, "Count");
            Assert.IsInstanceOfType(events[0], typeof(DefinovanDodavatelEvent));
            var evnt = events[0] as DefinovanDodavatelEvent;
            Assert.AreEqual("D14", evnt.Kod, "Kod");
            Assert.AreEqual(false, evnt.Deaktivovan, "Deaktivovan");
            Assert.AreEqual("Opravar, s.r.o.", evnt.Nazev, "Nazev");
            Assert.AreEqual("111-84758", evnt.Dic, "Dic");
            Assert.AreEqual("84758", evnt.Ico, "Ico");
            Assert.AreEqual("Strojova 15, 111 00 Destna", string.Join(", ", evnt.Adresa), "Adresa");
        }

        private class TestExternalEventRepository : IExternalEventRepository
        {
            private Dictionary<string, List<object>> _events;

            public TestExternalEventRepository()
            {
                _events = new Dictionary<string, List<object>>();
            }

            public Task Save(object evnt, string streamName)
            {
                List<object> eventList;
                if (!_events.TryGetValue(streamName, out eventList))
                {
                    _events[streamName] = eventList = new List<object>();
                }
                eventList.Add(evnt);
                return TaskUtils.CompletedTask();
            }

            public IList<object> GetStreamEvents(string streamName)
            {
                List<object> eventList;
                if (_events.TryGetValue(streamName, out eventList))
                    return eventList.ToList();
                else
                    return new object[0];
            }
        }
    }
}
