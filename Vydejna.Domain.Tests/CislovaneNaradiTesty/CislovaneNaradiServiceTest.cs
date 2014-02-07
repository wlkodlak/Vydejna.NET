using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    public class CislovaneNaradiServiceTestBase
    {
        private TestExecutor _executor;
        private CislovaneNaradiService _svc;
        private Exception _caughtException;
        private TestRepository<CislovaneNaradi> _repository;
        private VirtualTime _time;
        private Dictionary<string, int> _newEventCounts;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2012, 1, 18, 8, 19, 21));
            _executor = new TestExecutor();
            _repository = new TestRepository<CislovaneNaradi>();
            _svc = new CislovaneNaradiService(_repository, _time);
            _newEventCounts = null;
        }

        protected void Execute<T>(T cmd)
        {
            var mre = new ManualResetEventSlim();
            var msg = new CommandExecution<T>(cmd, () => mre.Set(), ex => { _caughtException = ex; mre.Set(); });
            ((IHandle<CommandExecution<T>>)_svc).Handle(msg);
            for (int i = 0; i < 3; i++)
            {
                _executor.Process();
                if (mre.Wait(10))
                    break;
            }
        }

        protected void Exception<TException>()
        {
            Assert.IsNotNull(_caughtException, "Expected exception");
            if (_caughtException is TException)
                return;
            throw _caughtException.PreserveStackTrace();
        }

        protected void Given(Guid naradiId, params object[] events)
        {
            _repository.AddEvents(naradiId, events);
        }

        protected IList<string> NewEventsTypeNames()
        {
            return _repository.NewEvents().Select(e => e.GetType().Name).ToList();
        }

        protected T NewEventOfType<T>()
        {
            return _repository.NewEvents().OfType<T>().FirstOrDefault();
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
            if (_newEventCounts == null)
            {
                _newEventCounts = _repository.NewEvents()
                    .Select(e => e.GetType().Name)
                    .GroupBy(s => s)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionary(g => g.Key, g => g.Count);
            }
        }

        /*
         * - prijem z vyroby
         *   - cislovane naradi musi existovat
         *   - cena nesmi byt zaporna
         *   - nutne zadat kod pracoviste
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - pokud stav naradi je v poradku, vada nesmi byt zadana
         *   - pokud stav naradi neni v poradku, vada musi byt urcena
         *   - naradi musi byt dostupne pro prijem z pracoviste
         *     - spravne pracoviste
         *   - naradi nesmi byt nedostupne pro prijem z pracoviste
         *     - jeste vubec nevydano
         *     - jine pracoviste
         */
        /*
         * - sesrotovani
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro srotovani
         *     - prijate z vyroby jako neopravitelne
         *   - naradi nesmi byt nedostupne pro srotovani
         *     - prijate z opravy jako opravene
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         */
        /*
         * - vydej na opravu
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro opravu
         *     - prijate z vyroby jako nutne opravit
         *   - naradi nesmi byt nedostupne pro opravu
         *     - prijate z vyroby jako v poradku
         *   - cena nesmi byt zaporna
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - je nutne zadat objednavku
         *   - je nutne zadat dodavatele
         *   - termin dodani musi byt v budoucnosti (relativne k datu operace)
         */
        /*
         * - prijem z opravy
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro prijem z opravy
         *     - vydane na opravu na stejnou objednavku
         *   - naradi nesmi byt nedostupne pro prijem z opravy
         *     - vydane na reklamaci na jine objednavce
         *   - cena nesmi byt zaporna
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - je nutne zadat objednavku
         *   - je nutne zadat dodavatele
         *   - je nutne zadat dodaci list
         */
    }
}
