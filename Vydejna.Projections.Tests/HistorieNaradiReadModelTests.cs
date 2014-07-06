using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Projections.HistorieNaradiReadModel;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class HistorieNaradiReadModelTest_Reader : HistorieNaradiReadModelTestBase
    {
        protected override void Given()
        {
            var zakladniDatum = new DateTime(2014, 1, 1);
            for (int i = 0; i < 380; i++)
            {
                var cisloNaradi = (i % 3 == 0) ? i % 18 + 1 : 0;
                var typOperace = (i / 3) % 6;

                var operace = new HistorieNaradiOperace();
                operace.EventId = Guid.NewGuid();
                operace.Datum = zakladniDatum.AddDays(i / 10).AddHours(6 + i % 10);
                operace.CisloNaradi = cisloNaradi == 0 ? (int?)null : cisloNaradi;
                operace.Pocet = cisloNaradi == 0 ? 1 : 5;
                PouzitNaradi(operace, (i / 7) % 3);
                PouzitCenu(operace, 0, 20);
                switch (typOperace)
                {
                    case 0:
                        PouzitTypOperace(operace, cisloNaradi != 0, "PrijatoNaVydejnu", "Příjem na výdejnu");
                        PouzitDodavatele(operace, i % 7);
                        break;
                    case 1:
                        PouzitTypOperace(operace, cisloNaradi != 0, "VydanoDoVyroby", "Výdej do výroby");
                        PouzitPracoviste(operace, i % 5);
                        break;
                    case 2:
                        PouzitTypOperace(operace, cisloNaradi != 0, "PrijatoZVyroby", "Příjem z výroby");
                        PouzitPracoviste(operace, i % 5);
                        break;
                    case 3:
                        PouzitTypOperace(operace, cisloNaradi != 0, "PredanoKOprave", "Výdej na opravu");
                        PouzitDodavatele(operace, i % 7);
                        PouzitObjednavku(operace, i / 5 % 29);
                        break;
                    case 4:
                        PouzitTypOperace(operace, cisloNaradi != 0, "PrijatoZOpravy", "Příjem z opravy");
                        PouzitDodavatele(operace, i % 7);
                        PouzitObjednavku(operace, i / 5 % 29);
                        break;
                    case 5:
                        PouzitTypOperace(operace, cisloNaradi != 0, "PredanoKeSesrotovani", "Výdej do šrotu");
                        break;
                }
                _dbOperaci.Data.Add(operace);
            }
        }

        private void PouzitNaradi(HistorieNaradiOperace operace, int index)
        {
            switch (index)
            {
                default:
                    operace.NaradiId = new Guid("38294be3-3892-bead-bbbb-3782ae37b102");
                    operace.Vykres = "182-3394";
                    operace.Rozmer = "30x10";
                    break;
                case 1:
                    operace.NaradiId = new Guid("8392a387-2be8-ac30-b394-abc939024682");
                    operace.Vykres = "204-9910";
                    operace.Rozmer = "prum. 5";
                    break;
                case 2:
                    operace.NaradiId = new Guid("891cb194-0873-81ab-832b-743bac73be83");
                    operace.Vykres = "350-1933";
                    operace.Rozmer = "100x4x4";
                    break;
            }
        }

        private void PouzitTypOperace(HistorieNaradiOperace operace, bool cislovane, string udalost, string nazev)
        {
            operace.TypUdalosti = string.Concat(cislovane ? "CislovaneNaradi" : "NecislovaneNaradi", udalost, "Event");
            operace.NazevOperace = nazev;
        }

        private void PouzitDodavatele(HistorieNaradiOperace operace, int indexDodavatele)
        {
            operace.KodDodavatele = "D" + indexDodavatele.ToString();
            operace.NazevDodavatele = "Dodavatel " + indexDodavatele.ToString();
        }

        private void PouzitCenu(HistorieNaradiOperace operace, decimal puvodni, decimal nova)
        {
            operace.PuvodniCelkovaCena = operace.Pocet * puvodni;
            operace.NovaCelkovaCena = operace.Pocet * nova;
        }

        private void PouzitPracoviste(HistorieNaradiOperace operace, int index)
        {
            switch (index)
            {
                default:
                case 0:
                    operace.KodPracoviste = "03839110";
                    operace.NazevPracoviste = "Brouseni";
                    break;
                case 1:
                    operace.KodPracoviste = "18394220";
                    operace.NazevPracoviste = "Lesteni";
                    break;
                case 2:
                    operace.KodPracoviste = "253814330";
                    operace.NazevPracoviste = "Kaleni";
                    break;
                case 3:
                    operace.KodPracoviste = "389428440";
                    operace.NazevPracoviste = "Rezani";
                    break;
                case 4:
                    operace.KodPracoviste = "498269550";
                    operace.NazevPracoviste = "Svarovani";
                    break;
            }
        }

        private void PouzitObjednavku(HistorieNaradiOperace operace, int index)
        {
            operace.CisloObjednavky = (index * 219 % 384).ToString() + "/2014";
        }

        [TestMethod]
        public void Stranka2()
        {
            ZiskatHistorii(new HistorieNaradiRequest
            {
                DatumOd = DateTime.MinValue,
                DatumDo = DateTime.MaxValue,
                Stranka = 2
            });
            Assert.IsNotNull(_response, "Response");
            Assert.AreEqual(380, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, _response.PocetStranek, "PocetStranek");
            Assert.IsNotNull(_response.Filtr, "Filtr");
            Assert.AreEqual(2, _response.Filtr.Stranka, "Stranka");
            Assert.AreEqual(100, _response.SeznamOperaci.Count, "Count");
            Assert.IsTrue(_response.SeznamOperaci.TrueForAll(h => h.Datum >= new DateTime(2014, 1, 18) && h.Datum.Date <= new DateTime(2014, 1, 28)));
        }

        [TestMethod]
        public void Stranka4()
        {
            ZiskatHistorii(new HistorieNaradiRequest
            {
                TypFiltru = HistorieNaradiTypFiltru.Vsechno,
                DatumOd = DateTime.MinValue,
                DatumDo = DateTime.MaxValue,
                Stranka = 4
            });
            Assert.IsNotNull(_response, "Response");
            Assert.AreEqual(380, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, _response.PocetStranek, "PocetStranek");
            Assert.IsNotNull(_response.Filtr, "Filtr");
            Assert.AreEqual(4, _response.Filtr.Stranka, "Stranka");
            Assert.AreEqual(80, _response.SeznamOperaci.Count, "Count");
            Assert.IsTrue(_response.SeznamOperaci.TrueForAll(h => h.Datum >= new DateTime(2014, 1, 1) && h.Datum.Date <= new DateTime(2014, 1, 8)));
        }

        [TestMethod]
        public void FiltrPodleCislaNaradi()
        {
            ZiskatHistorii(new HistorieNaradiRequest
            {
                TypFiltru = HistorieNaradiTypFiltru.CislovaneNaradi,
                DatumOd = DateTime.MinValue,
                DatumDo = DateTime.MaxValue,
                Stranka = 1,
                CisloNaradi = 1
            });
            Assert.IsNotNull(_response, "Response");
            Assert.IsTrue(_response.SeznamOperaci.TrueForAll(h => h.CisloNaradi == 1));
            var ocekavanyPocet = _dbOperaci.Data.Count(h => h.CisloNaradi == 1);
            Assert.AreEqual(ocekavanyPocet, _response.PocetCelkem, "PocetCelkem");
        }
    }

    [TestClass]
    public class HistorieNaradiReadModelTest_ProjectionBase : HistorieNaradiReadModelTestBase
    {
        [TestMethod]
        public void PrijemNaVydejnuBezDefinic()
        {
            var id = _build.Naradi("A").Prijmout().Necislovane(5).Dodavatel("D34").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
        }

        [TestMethod]
        public void PrijemNaVydejnuNormalne()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Naradi("B").Definovano("Chyba", "Chyba", "Chyba");
            _build.Dodavatel("D39", "Dodavatel 1");
            _build.Dodavatel("D00", "Chyba");
            var id = _build.Naradi("A").Prijmout().Necislovane(5).Dodavatel("D39").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(5, operace.Pocet, "Pocet");
            Assert.AreEqual(null, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("D39", operace.KodDodavatele, "KodDodavatele");
            Assert.AreEqual("Dodavatel 1", operace.NazevDodavatele, "NazevDodavatele");
            Assert.AreEqual("NecislovaneNaradiPrijatoNaVydejnuEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void PrijemCislovanehoNaradiNaVydejnu()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Dodavatel("D39", "Dodavatel 1");
            var id = _build.Naradi("A").Prijmout().Cislovane(3).Dodavatel("D39").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(CurrentTime, operace.Datum, "Datum");
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(1, operace.Pocet, "Pocet");
            Assert.AreEqual(3, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("D39", operace.KodDodavatele, "KodDodavatele");
            Assert.AreEqual("Dodavatel 1", operace.NazevDodavatele, "NazevDodavatele");
            Assert.AreEqual("CislovaneNaradiPrijatoNaVydejnuEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void VydejNaradiNaNeexistujiciPracoviste()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            var id = _build.Naradi("A").Vydat().Necislovane(1).Pracoviste("18472230").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual("18472230", operace.KodPracoviste, "KodPracoviste");
        }

        [TestMethod]
        public void VydejNaradiNaPracoviste()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Pracoviste("18472230", "Brouseni", "230");
            _build.Pracoviste("82748120", "Kaleni", "120");
            var id = _build.Naradi("A").Vydat().Necislovane(4).Pracoviste("82748120").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(4, operace.Pocet, "Pocet");
            Assert.AreEqual(null, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("82748120", operace.KodPracoviste, "KodPracoviste");
            Assert.AreEqual("Kaleni", operace.NazevPracoviste, "NazevPracoviste");
            Assert.AreEqual("NecislovaneNaradiVydanoDoVyrobyEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void PrijemCislovanehoVcetneVady()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Pracoviste("18472230", "Brouseni", "230");
            _build.Vada("0", "bez vady");
            _build.Vada("1", "opotrebovane");
            _build.Vada("2", "vyrobni defekt");
            var id = _build.Naradi("A").Vratit().Cislovane(11).Pracoviste("18472230").Poskozene("1").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(1, operace.Pocet, "Pocet");
            Assert.AreEqual(11, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("18472230", operace.KodPracoviste, "KodPracoviste");
            Assert.AreEqual("Brouseni", operace.NazevPracoviste, "NazevPracoviste");
            Assert.AreEqual("1", operace.KodVady, "KodVady");
            Assert.AreEqual("opotrebovane", operace.NazevVady, "NazevVady");
            Assert.AreEqual(StavNaradi.NutnoOpravit, operace.StavNaradi, "StavNaradi");
            Assert.AreEqual("CislovaneNaradiPrijatoZVyrobyEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void VydejNaradiKOprave()
        {
            CurrentTime = new DateTime(2014,6,22,15,22,6);
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Dodavatel("D28", "Naradi s.r.o.");
            var id = _build.Naradi("A").DatOpravit().Necislovane(10)
                .Objednavka("D28", "184/2014").TerminDodani(CurrentTime.Date.AddDays(15)).Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(10, operace.Pocet, "Pocet");
            Assert.AreEqual(null, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("D28", operace.KodDodavatele, "KodDodavatele");
            Assert.AreEqual("Naradi s.r.o.", operace.NazevDodavatele, "NazevDodavatele");
            Assert.AreEqual("184/2014", operace.CisloObjednavky, "CisloObjednavky");
            Assert.AreEqual("NecislovaneNaradiPredanoKOpraveEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void PrijemOpravenehoCislovanehoNaradi()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            _build.Dodavatel("D28", "Naradi s.r.o.");
            var id = _build.Naradi("A").Opravit().Cislovane(8).Opravene()
                .Objednavka("D28", "184/2014").DodaciList("D190/2014").Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(1, operace.Pocet, "Pocet");
            Assert.AreEqual(8, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("D28", operace.KodDodavatele, "KodDodavatele");
            Assert.AreEqual("Naradi s.r.o.", operace.NazevDodavatele, "NazevDodavatele");
            Assert.AreEqual("184/2014", operace.CisloObjednavky, "CisloObjednavky");
            Assert.AreEqual(StavNaradi.VPoradku, operace.StavNaradi, "StavNaradi");
            Assert.AreEqual("CislovaneNaradiPrijatoZOpravyEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }

        [TestMethod]
        public void SrotovaniNaradi()
        {
            _build.Naradi("A").Definovano("382-1193", "30x30", "Bruska");
            var id = _build.Naradi("A").Srotovat().Necislovane(3).Send();
            var operace = ZiskatOperaci(id);
            Assert.AreEqual(NaradiTestEventBuilder.BuildNaradiId("A"), operace.NaradiId, "NaradiId");
            Assert.AreEqual("382-1193", operace.Vykres, "Vykres");
            Assert.AreEqual("30x30", operace.Rozmer, "Rozmer");
            Assert.AreEqual(3, operace.Pocet, "Pocet");
            Assert.AreEqual(null, operace.CisloNaradi, "CisloNaradi");
            Assert.AreEqual("NecislovaneNaradiPredanoKeSesrotovaniEvent", operace.TypUdalosti, "TypUdalosti");
            Assert.IsFalse(string.IsNullOrEmpty(operace.NazevOperace), "NazevOperace");
        }
    }


    public class HistorieNaradiReadModelTestBase : ReadModelTestBase
    {
        protected TestRepositoryOperaci _dbOperaci;
        protected HistorieNaradiResponse _response;
        protected ProjectionTestEventBuilder _build;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _response = null;
            _dbOperaci = new TestRepositoryOperaci();
            _build = new ProjectionTestEventBuilder(this);
        }

        protected override IEventProjection CreateProjection()
        {
            return new HistorieNaradiProjection(_dbOperaci, new HistorieNaradiRepositoryPomocne(_folder), _time);
        }

        protected override object CreateReader()
        {
            return new HistorieNaradiReader(_dbOperaci);
        }

        protected void ZiskatHistorii(HistorieNaradiRequest request)
        {
            _response = ReadProjection<HistorieNaradiRequest, HistorieNaradiResponse>(request);
        }

        protected HistorieNaradiOperace ZiskatOperaci(Guid eventId)
        {
            Flush();
            var operace = _dbOperaci.Data.FirstOrDefault(h => h.EventId == eventId);
            Assert.IsNotNull(operace, "Operace {0}", eventId);
            return operace;
        }

        public class TestRepositoryOperaci : IHistorieNaradiRepositoryOperace
        {
            private readonly List<HistorieNaradiOperace> _data = new List<HistorieNaradiOperace>();

            public List<HistorieNaradiOperace> Data { get { return _data; } }

            public Task Reset()
            {
                _data.Clear();
                return TaskUtils.CompletedTask();
            }

            public Task UlozitNovouOperaci(HistorieNaradiOperace operace)
            {
                _data.Add(operace);
                return TaskUtils.CompletedTask();
            }

            public Task StornovatOperaci(Guid eventId)
            {
                foreach (var operace in _data)
                {
                    if (operace.EventId == eventId)
                    {
                        operace.Stornovano = true;
                    }
                }
                return TaskUtils.CompletedTask();
            }

            public Task<Tuple<int, List<HistorieNaradiOperace>>> Najit(HistorieNaradiRequest filtr)
            {
                var filtrovanyDotaz = _data.Where(o => filtr.DatumOd <= o.Datum && o.Datum <= filtr.DatumDo);
                switch (filtr.TypFiltru)
                {
                    case HistorieNaradiTypFiltru.CislovaneNaradi:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.CisloNaradi == filtr.CisloNaradi);
                        break;
                    case HistorieNaradiTypFiltru.Naradi:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.NaradiId == filtr.NaradiId);
                        break;
                    case HistorieNaradiTypFiltru.Objednavka:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.KodDodavatele == filtr.KodDodavatele && o.CisloObjednavky == filtr.CisloObjednavky);
                        break;
                    case HistorieNaradiTypFiltru.Pracoviste:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.KodPracoviste == filtr.KodPracoviste);
                        break;
                }
                var filtrovanySeznam = filtrovanyDotaz.OrderByDescending(o => o.Datum).ToList();
                var pocetNalezenych = filtrovanySeznam.Count;
                var vyslednySeznam = filtrovanySeznam.Skip(filtr.Stranka * 100 - 100).Take(100).ToList();
                return TaskUtils.FromResult(Tuple.Create(pocetNalezenych, vyslednySeznam));
            }
        }
    }
}
