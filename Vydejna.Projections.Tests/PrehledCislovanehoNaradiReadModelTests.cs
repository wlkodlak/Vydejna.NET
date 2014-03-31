using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Vydejna.Contracts;
using Vydejna.Projections.PrehledCislovanehoNaradiReadModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class PrehledCislovanehoNaradiReadModelTests : ReadModelTestBase
    {
        [TestMethod]
        public void NeexistujiciProjekceJePrazdnySeznam()
        {
            ZiskatStranku();
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(0, _response.PocetStranek, "PocetStranek");
            Assert.IsNotNull(_response.Seznam, "Seznam");
            Assert.AreEqual(0, _response.Seznam.Count, "Seznam.Count");
        }

        [TestMethod]
        public void NoveCislovaneNaradiMaUdajeZUdalostiPrijmu()
        {
            SendDefinovanoNaradi("A");
            SendEvent(new CislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = NaradiId("A"),
                CisloNaradi = 38,
                Datum = CurrentTime,
                Verze = 1,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                EventId = Guid.NewGuid(),
                KodDodavatele = "D005",
                PrijemZeSkladu = false,
                CenaPredchozi = 0m,
                CenaNova = 5m
            });
            var naradi = ZiskatNaradi("A", 38);
            Assert.AreEqual(5m, naradi.Cena, "Cena");
            Assert.AreEqual(new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" }, naradi.Umisteni, "Umisteni");
        }

        [TestMethod]
        public void NoveCislovaneNaradiMaUdajeZDefiniceNaradi()
        {
            SendDefinovanoNaradi("A", "809-7247", "prum. 35", "operka");
            SendPrijemNoveho("A", 38);
            var naradi = ZiskatNaradi("A", 38);
            Assert.AreEqual("809-7247", naradi.Vykres, "Vykres");
            Assert.AreEqual("prum. 35", naradi.Rozmer, "Rozmer");
            Assert.AreEqual("operka", naradi.Druh, "Druh");
        }

        [TestMethod]
        public void VydejDoVyrobuUpravujeUmisteniNaradiACenu()
        {
            SendDefinovanoNaradi("A");
            SendPrijemNoveho("A", 13);
            SendVydanoDoVyroby("A", 13, 18m, "85742310");
            var naradi = ZiskatNaradi("A", 13);
            Assert.AreEqual(18m, naradi.Cena, "Cena");
            Assert.AreEqual(UmisteniPracoviste("85742310"), naradi.Umisteni, "Umisteni");
        }

        [TestMethod]
        public void PrijemZVyrobyUpravujeUmisteniNaradiACenu()
        {
            SendDefinovanoNaradi("A");
            SendPrijemNoveho("A", 13);
            SendVydanoDoVyroby("A", 13, 18m, "85742310");
            SendPrijatoZVyroby("A", 13, StavNaradi.NutnoOpravit, novaCena: 18m);
            var naradi = ZiskatNaradi("A", 13);
            Assert.AreEqual(18m, naradi.Cena, "Cena");
            Assert.AreEqual(UmisteniVydejna(StavNaradi.NutnoOpravit), naradi.Umisteni, "Umisteni");
        }

        [TestMethod]
        public void VydejDoOpravyUpravujeUmisteniNaradiACenu()
        {
            SendDefinovanoNaradi("A");
            SendPrijemNoveho("A", 13);
            SendVydanoDoVyroby("A", 13, 18m, "85742310");
            SendPrijatoZVyroby("A", 13, StavNaradi.NutnoOpravit);
            SendPredanoKOprave("A", 13, 0m, "D006", "847/2014");
            var naradi = ZiskatNaradi("A", 13);
            Assert.AreEqual(0m, naradi.Cena, "Cena");
            Assert.AreEqual(UmisteniOprava("D006", "847/2014"), naradi.Umisteni, "Umisteni");
        }

        [TestMethod]
        public void PrijemZOpravyUpravujeUmisteniNaradiACenu()
        {
            SendDefinovanoNaradi("A");
            SendPrijemNoveho("A", 13);
            SendVydanoDoVyroby("A", 13);
            SendPrijatoZVyroby("A", 13, StavNaradi.NutnoOpravit);
            SendPredanoKOprave("A", 13);
            SendPrijatoZOpravy("A", 13, StavNaradiPoOprave.Neopravitelne, 10m);
            var naradi = ZiskatNaradi("A", 13);
            Assert.AreEqual(10m, naradi.Cena, "Cena");
            Assert.AreEqual(UmisteniVydejna(StavNaradi.Neopravitelne), naradi.Umisteni, "Umisteni");
        }

        [TestMethod]
        public void VydejDoSrotuOdstranujeNaradiZeSeznamu()
        {
            SendDefinovanoNaradi("A");
            SendPrijemNoveho("A", 13);
            SendVydanoDoVyroby("A", 13);
            SendPrijatoZVyroby("A", 13, StavNaradi.Neopravitelne);
            SendSrotovano("A", 13);
            ZiskatStranku();
            Assert.IsNull(_response.Seznam.FirstOrDefault(n => n.NaradiId == NaradiId("A") && n.CisloNaradi == 13));
        }

        [TestMethod]
        public void SeznamJeSerazenVzestupnePodleCislaNaradi()
        {
            SendDefinovanoNaradi("A");
            SendDefinovanoNaradi("B");
            SendDefinovanoNaradi("C");
            SendPrijemNoveho("C", 3);
            SendPrijemNoveho("C", 7);
            SendPrijemNoveho("B", 4);
            SendPrijemNoveho("A", 5);
            SendPrijemNoveho("B", 2);
            SendPrijemNoveho("A", 1);
            SendPrijemNoveho("B", 6);
            ZiskatStranku();
            for (int i = 1; i < _response.Seznam.Count; i++)
            {
                Assert.IsTrue(_response.Seznam[i - 1].CisloNaradi < _response.Seznam[i].CisloNaradi);
            }
        }

        [TestMethod]
        public void DlouhySeznamJeStrankovan()
        {
            SendDefinovanoNaradi("A");
            for (int d = 0; d < 4; d++)
            {
                for (int i = 0; i < 324; i++)
                {
                    if (d == (i & 3))
                        SendPrijemNoveho("A", i + 1);
                }
            }
            ZiskatStranku(2);
            Assert.AreEqual(2, _response.Stranka);
            Assert.AreEqual(324, _response.PocetCelkem);
            Assert.AreEqual(4, _response.PocetStranek);
            Assert.AreEqual(100, _response.Seznam.Count);
            Assert.AreEqual(101, _response.Seznam[0].CisloNaradi);
            Assert.AreEqual(200, _response.Seznam[99].CisloNaradi);
        }

        private static UmisteniNaradiDto UmisteniVydejna(StavNaradi stavNaradi)
        {
            return new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = stavNaradi.ToString() };
        }

        private static UmisteniNaradiDto UmisteniPracoviste(string pracoviste)
        {
            return new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste };
        }

        private static UmisteniNaradiDto UmisteniOprava(string dodavatel, string objednavka)
        {
            return new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, Dodavatel = dodavatel, Objednavka = objednavka, UpresneniZakladu = "Oprava" };
        }

        private static UmisteniNaradiDto UmisteniSrot()
        {
            return new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSrotu };
        }

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _response = null;
            _verzeNaradi = 2;
        }

        private PrehledCislovanehoNaradiResponse _response;
        private int _verzeNaradi;

        private void SendDefinovanoNaradi(string naradiIdBase)
        {
            switch (naradiIdBase)
            {
                case "A":
                    SendDefinovanoNaradi("A", "809-7247", "prum. 35", "operka");
                    break;
                case "B":
                    SendDefinovanoNaradi("B", "721-2715b", "47x5", "drzak");
                    break;
                case "C":
                    SendDefinovanoNaradi("C", "1-2748-002", "prum. 55", "brusny kotouc");
                    break;
            }
        }

        private static Guid NaradiId(string naradiIdBase)
        {
            return new Guid("7777000" + naradiIdBase + "-0000-0000-0000-000000000000");
        }

        private void SendDefinovanoNaradi(string naradiIdBase, string vykres, string rozmer, string druh)
        {
            SendEvent(new DefinovanoNaradiEvent
            {
                NaradiId = NaradiId(naradiIdBase),
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh,
                Verze = 1
            });
        }

        private void SendPrijemNoveho(string naradiIdBase, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Datum = CurrentTime,
                Verze = 2,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu },
                NoveUmisteni = UmisteniVydejna(StavNaradi.VPoradku),
                EventId = Guid.NewGuid(),
                KodDodavatele = "D005",
                PrijemZeSkladu = false,
                CenaPredchozi = 0m,
                CenaNova = 5m
            });
        }

        private void SendVydanoDoVyroby(string naradiIdBase, int cisloNaradi, decimal novaCena = 5m, string pracoviste = "84772844")
        {
            SendEvent(new CislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Verze = _verzeNaradi++,
                Datum = CurrentTime,
                KodPracoviste = pracoviste,
                CenaPredchozi = 3m,
                CenaNova = novaCena,
                PredchoziUmisteni = UmisteniVydejna(StavNaradi.VPoradku),
                NoveUmisteni = UmisteniPracoviste(pracoviste)
            });
        }

        private void SendPrijatoZVyroby(string naradiIdBase, int cisloNaradi, StavNaradi stavNaradi, string kodVady = "9", decimal novaCena = 1m, string pracoviste = "84772844")
        {
            SendEvent(new CislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Verze = _verzeNaradi++,
                Datum = CurrentTime,
                KodPracoviste = pracoviste,
                CenaPredchozi = 5m,
                CenaNova = novaCena,
                PredchoziUmisteni = UmisteniPracoviste(pracoviste),
                StavNaradi = stavNaradi,
                NoveUmisteni = UmisteniVydejna(stavNaradi),
                KodVady = kodVady
            });
        }

        private void SendPredanoKOprave(string naradiIdBase, int cisloNaradi, decimal novaCena = 6m, string dodavatel = "D005", string objednavka = "847/2014")
        {
            SendEvent(new CislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Verze = _verzeNaradi++,
                Datum = CurrentTime,
                CenaPredchozi = 5m,
                CenaNova = novaCena,
                PredchoziUmisteni = UmisteniVydejna(StavNaradi.NutnoOpravit),
                NoveUmisteni = UmisteniOprava(dodavatel, objednavka),
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TerminDodani = CurrentTime.Date.AddDays(30),
                TypOpravy = TypOpravy.Oprava
            });
        }

        private void SendPrijatoZOpravy(string naradiIdBase, int cisloNaradi, StavNaradiPoOprave novyStav = StavNaradiPoOprave.Opraveno,
            decimal novaCena = 6m, string dodavatel = "D005", string objednavka = "847/2014")
        {
            SendEvent(new CislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Verze = _verzeNaradi++,
                Datum = CurrentTime,
                CenaPredchozi = 5m,
                CenaNova = novaCena,
                PredchoziUmisteni = UmisteniOprava(dodavatel, objednavka),
                NoveUmisteni = UmisteniVydejna(StavNaradi.Neopravitelne),
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TypOpravy = TypOpravy.Oprava, DodaciList = "D" + objednavka, Opraveno = novyStav, 
                StavNaradi = StavNaradiPodleOpravy(novyStav)
            });
        }

        private static StavNaradi StavNaradiPodleOpravy(StavNaradiPoOprave novyStav)
        {
            switch (novyStav)
            {
                case StavNaradiPoOprave.Neopravitelne:
                    return StavNaradi.Neopravitelne;
                case StavNaradiPoOprave.OpravaNepotrebna:
                case StavNaradiPoOprave.Opraveno:
                    return StavNaradi.VPoradku;
                default:
                    return StavNaradi.Neurcen;
            }
        }

        private void SendSrotovano(string naradiIdBase, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiPredanoKeSesrotovaniEvent
            {
                EventId = Guid.NewGuid(),
                NaradiId = NaradiId(naradiIdBase),
                CisloNaradi = cisloNaradi,
                Verze = _verzeNaradi++,
                Datum = CurrentTime,
                CenaPredchozi = 5m,
                CenaNova = 0m,
                PredchoziUmisteni = UmisteniVydejna(StavNaradi.NutnoOpravit),
                NoveUmisteni = UmisteniSrot()
            });
        }

        private void ZiskatStranku(int stranka = 1)
        {
            _response = ReadProjection<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>(
                new PrehledCislovanehoNaradiRequest { Stranka = stranka });
        }

        private CislovaneNaradiVPrehledu ZiskatNaradi(string naradiIdBase, int cisloNaradi)
        {
            if (_response == null)
                ZiskatStranku();
            var naradiId = NaradiId(naradiIdBase);
            var naradi = _response.Seznam.Where(n => n.NaradiId == naradiId && n.CisloNaradi == cisloNaradi).FirstOrDefault();
            Assert.IsNotNull(naradi, "Naradi {0} nenalezeno v seznamu", cisloNaradi);
            return naradi;
        }

        protected override IEventProjection CreateProjection()
        {
            return new PrehledCislovanehoNaradiProjection(new PrehledCislovanehoNaradiRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new PrehledCislovanehoNaradiReader(new PrehledCislovanehoNaradiRepository(_folder), _executor, _time);
        }
    }
}
