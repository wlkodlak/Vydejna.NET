using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using Vydejna.Contracts;
using ServiceLib.Tests.TestUtils;
using System.Threading;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class PrijemNaVydejnuTest
    {
        private TestExecutor _executor;
        private CislovaneNaradiService _svc;
        private Exception _caughtException;
        private TestRepository<CislovaneNaradi> _repository;
        private VirtualTime _time;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2012, 1, 18, 8, 19, 21));
            _executor = new TestExecutor();
            _repository = new TestRepository<CislovaneNaradi>();
            _svc = new CislovaneNaradiService(_repository, _time);
        }

        private void Execute<T>(T cmd)
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

        private void Exception<TException>()
        {
            Assert.IsNotNull(_caughtException, "Expected exception");
            if (_caughtException is TException)
                return;
            throw _caughtException.PreserveStackTrace();
        }

        private class TestRepository<T> : IEventSourcedRepository<T>
            where T : class, IEventSourcedAggregate, new()
        {
            private Dictionary<Guid, List<object>> _allEvents;
            private List<object> _newEvents;
            private bool _throwConcurrency;

            public TestRepository()
            {
                _allEvents = new Dictionary<Guid, List<object>>();
                _newEvents = new List<object>();
            }

            public void AddEvents(Guid id, params object[] events)
            {
                List<object> all;
                if (!_allEvents.TryGetValue(id, out all))
                    _allEvents[id] = all = new List<object>();
                all.AddRange(events);
            }

            public void ThrowConcurrency()
            {
                _throwConcurrency = true;
            }

            public IList<object> NewEvents()
            {
                return _newEvents;
            }

            public void Load(Guid id, Action<T> onLoaded, Action onMissing, Action<Exception> onError)
            {
                List<object> all;
                if (!_allEvents.TryGetValue(id, out all))
                    onMissing();
                else
                {
                    var aggregate = new T();
                    aggregate.LoadFromEvents(all);
                    aggregate.CommitChanges(all.Count);
                    onLoaded(aggregate);
                }
            }

            public void Save(T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError)
            {
                try
                {
                    if (_throwConcurrency)
                        onConcurrency();
                    else
                    {
                        var events = aggregate.GetChanges();
                        var id = aggregate.Id;

                        List<object> all;
                        if (!_allEvents.TryGetValue(id, out all))
                            _allEvents[id] = all = new List<object>();
                        all.AddRange(events);
                        _newEvents.AddRange(events);

                        aggregate.CommitChanges(all.Count);
                    }
                }
                catch (Exception ex)
                {
                    onError(ex);
                    return;
                }
                onSaved();
            }
        }

        [TestMethod]
        public void CisloNaradiMusiBytKladne()
        {
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = new Guid("87228724-1111-1111-1111-222233334444"),
                CenaNova = 5,
                CisloNaradi = -1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = new Guid("87228724-1111-1111-1111-222233334444"),
                CenaNova = -5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            Exception<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void CisloNaradiNesmiBytObsazeno()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            _repository.AddEvents(naradiId,
                new CislovaneNaradiPrijatoNaVydejnuEvent
                {
                    NaradiId = naradiId,
                    CisloNaradi = 1,
                    EventId = Guid.NewGuid(),
                    Datum = new DateTime(2012, 1, 1),
                    KodDodavatele = "E7",
                    PrijemZeSkladu = false,
                    CenaNova = 3,
                    NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto()
                });
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CisloNaradi = 1,
                CenaNova = 5,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            Exception<InvalidOperationException>();
        }

        [TestMethod]
        public void GenerujeSePrijatoZeSkladu()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            var realne = string.Join(", ", _repository.NewEvents().Select(e => e.GetType().Name));
            Assert.AreEqual("CislovaneNaradiPrijatoNaVydejnuEvent", realne);
        }

        [TestMethod]
        public void DoUdalostiSeGenerujiAutomatickeVlastnosti()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            var udalost = _repository.NewEvents().OfType<CislovaneNaradiPrijatoNaVydejnuEvent>().FirstOrDefault();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(_time.GetUtcTime(), udalost.Datum, "Datum");
        }

        [TestMethod]
        public void DoUdalostiSeDoplnujiDataZPrijazu()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = _repository.NewEvents().OfType<CislovaneNaradiPrijatoNaVydejnuEvent>().FirstOrDefault();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.KodDodavatele, udalost.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.PrijemZeSkladu, udalost.PrijemZeSkladu, "PrijemZeSkladu");
        }

        [TestMethod]
        public void UmisteniNaradiNaVydejne()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = _repository.NewEvents().OfType<CislovaneNaradiPrijatoNaVydejnuEvent>().FirstOrDefault();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku), UmisteniNaradi.Dto(udalost.NoveUmisteni), "Umisteni");
        }

        [TestMethod]
        public void PriPrijmuZeSkladuSeSnizujeStavSkladuPomociInterniUdalosti()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = true
            };
            Execute(cmd);
            var udalost = _repository.NewEvents().OfType<NastalaPotrebaUpravitStavNaSkladeEvent>().FirstOrDefault();
            Assert.IsNotNull(udalost, "Ocekavana udalost pro zmenu stavu na sklade");
            Assert.AreEqual(naradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(TypZmenyNaSklade.SnizitStav, udalost.TypZmeny, "TypZmeny");
            Assert.AreEqual(1, udalost.Hodnota, "Hodnota");

        }
    }
    /*
     * - prijem na vydejnu
     *   - cena nesmi byt zaporna
     *   - cislo naradi nesmi byt obsazeno
     *   - v udalosti odpovidaji polozky prikazu
     *   - v udalosti se automaticky doplni EventId a Datum
     *   - pri prijmu ze skladu se generuje interni udalost pro zmenu stavu na sklade
     */
}
