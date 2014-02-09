using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    public abstract class ObecneNaradiServiceTestBase<TAggregate, TService>
        where TAggregate : class, IEventSourcedAggregate, new()
    {
        protected TestExecutor _executor;
        protected TService _svc;
        protected Exception _caughtException;
        protected TestRepository<TAggregate> _repository;
        protected Dictionary<string, int> _newEventCounts;
        protected VirtualTime _time;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2012, 1, 18, 8, 19, 21));
            _executor = new TestExecutor();
            _repository = new TestRepository<TAggregate>();
            _svc = CreateService();
            _newEventCounts = null;
        }

        protected abstract TService CreateService();

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

        protected void ChybaValidace(string kategorie, string polozka)
        {
            Assert.IsNotNull(_caughtException, "Ocekavana chyba validace {0} {1}", kategorie, polozka);
            var chybaValidace = _caughtException as ValidationErrorException;
            var chybaStavu = _caughtException as DomainErrorException;
            if (chybaValidace != null)
            {
                Assert.AreEqual(polozka, chybaValidace.Field, "Polozka chyby validace");
                Assert.AreEqual(kategorie, chybaValidace.Category, "Kategorie chyby validace");
                Assert.AreEqual("", string.Join(", ", NewEventsTypeNames()), "Udalosti");
            }
            else if (chybaStavu != null)
                Assert.Fail("Ocekavana chyba validace {0} {1}, nalezena chyba stavu {2} {3}",
                    kategorie, polozka, chybaStavu.Category, chybaStavu.Field);
            else
                throw _caughtException.PreserveStackTrace();
        }

        protected void ChybaStavu(string kategorie, string polozka)
        {
            Assert.IsNotNull(_caughtException, "Ocekavana chyba stavu {0} {1}", kategorie, polozka);
            var chybaStavu = _caughtException as DomainErrorException;
            var chybaValidace = _caughtException as ValidationErrorException;
            if (chybaStavu != null)
            {
                Assert.AreEqual(polozka, chybaStavu.Field, "Polozka chyby stavu");
                Assert.AreEqual(kategorie, chybaStavu.Category, "Kategorie chyby stavu");
                Assert.AreEqual("", string.Join(", ", NewEventsTypeNames()), "Udalosti");
            }
            else if (chybaValidace != null)
                Assert.Fail("Ocekavana chyba stavu {0} {1}, nalezena chyba validace {2} {3}",
                    kategorie, polozka, chybaValidace.Category, chybaValidace.Field);
            else
                throw _caughtException.PreserveStackTrace();
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
            var evnt = _repository.NewEvents().OfType<T>().FirstOrDefault();
            Assert.IsNotNull(evnt, "Expected event {0}", typeof(T).Name);
            return evnt;
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
            if (_caughtException != null)
                throw _caughtException.PreserveStackTrace();
            if (_newEventCounts == null)
            {
                _newEventCounts = _repository.NewEvents()
                    .Select(e => e.GetType().Name)
                    .GroupBy(s => s)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionary(g => g.Key, g => g.Count);
            }
        }


        protected CislovaneNaradiPrijatoNaVydejnuEvent EvtPrijato(Guid naradi, int cisloNaradi,
            string kodDodavatele = "D43", decimal cena = 283m)
        {
            return new CislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                KodDodavatele = kodDodavatele,
                CenaNova = cena,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                PrijemZeSkladu = false
            };
        }

        protected CislovaneNaradiVydanoDoVyrobyEvent EvtVydano(Guid naradi, int cisloNaradi,
            string pracoviste = "88339430", decimal cenaPred = 100m, decimal cenaPo = 100m)
        {
            return new CislovaneNaradiVydanoDoVyrobyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                KodPracoviste = pracoviste,
                NoveUmisteni = UmisteniNaradi.NaPracovisti(pracoviste).Dto()
            };
        }

        protected CislovaneNaradiPrijatoZVyrobyEvent EvtVraceno(Guid naradi, int cisloNaradi,
            string pracoviste = "88339430", StavNaradi stav = StavNaradi.VPoradku,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            string vada = null)
        {
            return new CislovaneNaradiPrijatoZVyrobyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodPracoviste = pracoviste,
                KodVady = vada,
                StavNaradi = stav,
                PredchoziUmisteni = UmisteniNaradi.NaPracovisti(pracoviste).Dto(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(stav).Dto()
            };
        }

        protected CislovaneNaradiPredanoKeSesrotovaniEvent EvtSrotovano(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, StavNaradi stav = StavNaradi.Neopravitelne)
        {
            return new CislovaneNaradiPredanoKeSesrotovaniEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(stav).Dto(),
                NoveUmisteni = UmisteniNaradi.VeSrotu().Dto()
            };
        }

        protected CislovaneNaradiPredanoKOpraveEvent EvtOprava(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            StavNaradi stav = StavNaradi.NutnoOpravit, TypOpravy typ = TypOpravy.Oprava,
            string dodavatel = "D43", string objednavka = "483/2013", DateTime termin = default(DateTime))
        {
            return new CislovaneNaradiPredanoKOpraveEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TerminDodani = termin == default(DateTime) ? GetUtcTime().Date.AddDays(15) : termin,
                TypOpravy = typ,
                PredchoziUmisteni = UmisteniNaradi.NaVydejne(stav).Dto(),
                NoveUmisteni = UmisteniNaradi.NaOprave(typ, dodavatel, objednavka).Dto()
            };
        }

        protected CislovaneNaradiPrijatoZOpravyEvent EvtOpraveno(Guid naradi, int cisloNaradi,
            decimal cenaPred = 100m, decimal cenaPo = 100m,
            TypOpravy typ = TypOpravy.Oprava, StavNaradiPoOprave opraveno = StavNaradiPoOprave.Opraveno,
            string dodavatel = "D43", string objednavka = "483/2013", string dodaciList = "483d/2013")
        {
            var stav = (opraveno == StavNaradiPoOprave.OpravaNepotrebna || opraveno == StavNaradiPoOprave.Opraveno) ? StavNaradi.VPoradku : StavNaradi.Neopravitelne;
            return new CislovaneNaradiPrijatoZOpravyEvent
            {
                NaradiId = naradi,
                CisloNaradi = cisloNaradi,
                Datum = GetUtcTime(),
                EventId = Guid.NewGuid(),
                CenaPredchozi = cenaPred,
                CenaNova = cenaPo,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TypOpravy = typ,
                DodaciList = dodaciList,
                Opraveno = opraveno,
                StavNaradi = stav,
                PredchoziUmisteni = UmisteniNaradi.NaOprave(typ, dodavatel, objednavka).Dto(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(stav).Dto()
            };
        }
    }

    public class CislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<CislovaneNaradi, CislovaneNaradiService>
    {
        protected override CislovaneNaradiService CreateService()
        {
            return new CislovaneNaradiService(_repository, _time);
        }
    }

    public class NecislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<NecislovaneNaradi, NecislovaneNaradiService>
    {
        protected override NecislovaneNaradiService CreateService()
        {
            return new NecislovaneNaradiService(_repository, _time);
        }
    }
}
