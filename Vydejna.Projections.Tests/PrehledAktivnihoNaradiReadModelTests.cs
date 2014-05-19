using System;
using System.Collections.Generic;
using System.Linq;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Vydejna.Contracts;
using Vydejna.Projections.PrehledAktivnihoNaradiReadModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class PrehledAktivnihoNaradiReadModelTests : ReadModelTestBase
    {
        [TestMethod]
        public void NeexistujiciProjekce()
        {
            ZiskatStranku();
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(0, _response.PocetStranek, "PocetStranek");
            Assert.IsNotNull(_response.Naradi, "Naradi");
            Assert.AreEqual(0, _response.Naradi.Count, "Naradi.Count");
        }

        [TestMethod]
        public void NoveDefinovaneNaradiSePridaDoSeznamu()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            ZiskatStranku();
            var naradi = _response.Naradi.FirstOrDefault(n => n.NaradiId == NaradiId("0001"));

            Assert.IsNotNull(naradi, "Existuje");
            Assert.AreEqual("584-5589-2", naradi.Vykres, "Vykres");
            Assert.AreEqual("prum. 15", naradi.Rozmer, "Rozmer");
            Assert.AreEqual("kotouc", naradi.Druh, "Druh");
        }

        [TestMethod]
        public void DeaktivovaneNaradiZmiziZeSeznamu()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendDeaktivovanoNaradi("0001");
            ZiskatStranku();
            var naradi = _response.Naradi.FirstOrDefault(n => n.NaradiId == NaradiId("0001"));
            Assert.IsNull(naradi);
        }

        [TestMethod]
        public void AktivovaneNaradiSeVraciDoSeznamu()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendDeaktivovanoNaradi("0001");
            SendAktivovanoNaradi("0001");
            ZiskatStranku();
            var naradi = _response.Naradi.FirstOrDefault(n => n.NaradiId == NaradiId("0001"));

            Assert.IsNotNull(naradi, "Existuje");
            Assert.AreEqual("584-5589-2", naradi.Vykres, "Vykres");
            Assert.AreEqual("prum. 15", naradi.Rozmer, "Rozmer");
            Assert.AreEqual("kotouc", naradi.Druh, "Druh");
        }

        [TestMethod]
        public void DefiniceNaradi_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            AssertPoctyNaradi("0001", 0, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void ZmenyNaSklade_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendZmenaNaSklade("0001", 5, 5);
            AssertPoctyNaradi("0001", 5, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void ZmenyNaSklade_B()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendZmenaNaSklade("0001", 5, 5);
            SendZmenaNaSklade("0001", -3, 2);
            AssertPoctyNaradi("0001", 2, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void ZmenyNaSklade_C()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendZmenaNaSklade("0001", 5, 5);
            SendZmenaNaSklade("0001", 2, 7);
            AssertPoctyNaradi("0001", 7, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PrijemNaVydejnu_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            AssertPoctyNaradi("0001", 0, 5, 0, 0, 0, 0);
        }

        [TestMethod]
        public void VydejDoVyroby_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            AssertPoctyNaradi("0001", 0, 1, 4, 0, 0, 0);
        }

        [TestMethod]
        public void VraceniNaradiVPoradku_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendVraceno("0001", 2, 2, 3);
            AssertPoctyNaradi("0001", 0, 3, 2, 0, 0, 0);
        }

        [TestMethod]
        public void VraceniPoskozenehoNaradi_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendPoskozene("0001", 2, 2, 2);
            AssertPoctyNaradi("0001", 0, 1, 2, 2, 0, 0);
        }

        [TestMethod]
        public void VraceniZnicenehoNaradi_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendZnicene("0001", 2, 2, 2);
            AssertPoctyNaradi("0001", 0, 1, 2, 0, 0, 2);
        }

        [TestMethod]
        public void OdeslaneKOprave_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendPoskozene("0001", 2, 2, 2);
            SendOdeslano("0001", 1, 1, 1);
            AssertPoctyNaradi("0001", 0, 1, 2, 1, 1, 0);
        }

        [TestMethod]
        public void Opravene_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendPoskozene("0001", 2, 2, 2);
            SendOdeslano("0001", 1, 1, 1);
            SendOpravene("0001", 1, 0, 2);
            AssertPoctyNaradi("0001", 0, 2, 2, 1, 0, 0);
        }

        [TestMethod]
        public void Neopravitelne_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendPoskozene("0001", 2, 2, 2);
            SendOdeslano("0001", 1, 1, 1);
            SendNeopravitelne("0001", 1, 0, 1);
            AssertPoctyNaradi("0001", 0, 1, 2, 1, 0, 1);
        }

        [TestMethod]
        public void Srotovane_A()
        {
            SendDefinovanoNaradi("0001", "584-5589-2", "prum. 15", "kotouc");
            SendPrijato("0001", 5, 5);
            SendVydano("0001", 4, 1, 4);
            SendZnicene("0001", 2, 2, 2);
            SendSrotovane("0001", 1, 1);
            AssertPoctyNaradi("0001", 0, 1, 2, 0, 0, 1);
        }

        [TestMethod]
        public void SeznamSerazenPodleVykresuARozmeru()
        {
            SendDefinovanoNaradi("0001", "584-5589", "prum. 15", "kotouc");
            SendDefinovanoNaradi("0002", "584-5589", "prum. 12", "kotouc");
            SendDefinovanoNaradi("0003", "111-2014", "prum. 15", "kotouc");
            SendDefinovanoNaradi("0004", "844-1248-0", "10x20", "kotouc");
            SendDefinovanoNaradi("0005", "584-1755a", "o 10", "kotouc");
            ZiskatStranku();
            var ocekavane = "0003, 0005, 0002, 0001, 0004";
            var realne = string.Join(", ", _response.Naradi.Select(n => n.NaradiId.ToString().Substring(4, 4)));
            Assert.AreEqual(ocekavane, realne);
        }

        protected void AssertPoctyNaradi(string naradiId, int sklad, int vporadku, int vyroba, int poskozene, int oprava, int znicene)
        {
            if (_response == null)
                ZiskatStranku(1);
            var naradi = _response.Naradi.FirstOrDefault(n => n.NaradiId == NaradiId(naradiId));
            Assert.IsNotNull(naradiId, "Naradi {0} nalezeno", naradiId);
            Assert.AreEqual(sklad, naradi.NaSklade, "NaSklade {0}", naradiId);
            Assert.AreEqual(vporadku, naradi.VPoradku, "VPoradku {0}", naradiId);
            Assert.AreEqual(vyroba, naradi.VeVyrobe, "VeVyrobe {0}", naradiId);
            Assert.AreEqual(poskozene, naradi.Poskozene, "Poskozene {0}", naradiId);
            Assert.AreEqual(oprava, naradi.Opravovane, "Opravovane {0}", naradiId);
            Assert.AreEqual(znicene, naradi.Znicene, "Znicene {0}", naradiId);
        }

        protected Guid NaradiId(string zaklad)
        {
            return new Guid("0000" + zaklad + "-0000-0000-0000-0000aaaabecd");
        }

        protected void SendDefinovanoNaradi(string naradiId, string vykres, string rozmer, string druh)
        {
            SendEvent(new DefinovanoNaradiEvent
            {
                NaradiId = NaradiId(naradiId),
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh,
                Verze = 1
            });
        }

        protected void SendDeaktivovanoNaradi(string naradiId)
        {
            SendEvent(new DeaktivovanoNaradiEvent
            {
                NaradiId = NaradiId(naradiId),
                Verze = 2
            });
        }

        protected void SendAktivovanoNaradi(string naradiId)
        {
            SendEvent(new AktivovanoNaradiEvent
            {
                NaradiId = NaradiId(naradiId),
                Verze = 3
            });
        }

        protected void SendDefinovanDodavatel(string kod, string nazev)
        {
            SendEvent(new DefinovanDodavatelEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovan = false,
                Ico = kod,
                Dic = kod,
                Adresa = new[] { nazev, "38001 Dacice" }
            });
        }

        protected void SendDefinovanoPracoviste(string kod, string nazev, string stredisko)
        {
            SendEvent(new DefinovanoPracovisteEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovano = false,
                Stredisko = stredisko
            });
        }

        protected void SendDefinovanaVada(string kod, string nazev)
        {
            SendEvent(new DefinovanaVadaNaradiEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovana = string.IsNullOrEmpty(nazev)
            });
        }

        protected void SendZmenaNaSklade(string naradi, int zmena, int novyStav)
        {
            SendEvent(new ZmenenStavNaSkladeEvent
            {
                NaradiId = NaradiId(naradi),
                DatumZmeny = CurrentTime,
                ZdrojZmeny = ZdrojZmenyNaSklade.Manualne,
                TypZmeny = zmena > 0 ? TypZmenyNaSklade.ZvysitStav : TypZmenyNaSklade.SnizitStav,
                NovyStav = novyStav,
                Hodnota = zmena > 0 ? zmena : -zmena,
                Verze = _verze++
            });
        }

        protected void SendPrijato(string naradi, int pocet, int vporadku, string dodavatel = "D001")
        {
            SendEvent(new NecislovaneNaradiPrijatoNaVydejnuEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                KodDodavatele = dodavatel,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = 0,
                PocetNaNovem = vporadku,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" }
            });
        }

        protected void SendVydano(string naradi, int pocet, int vporadku, int napracovisti, string pracoviste = "11111220")
        {
            SendEvent(new NecislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = vporadku,
                PocetNaNovem = napracovisti,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                KodPracoviste = pracoviste
            });
        }

        protected void SendVraceno(string naradi, int pocet, int napracovisti, int vporadku, string pracoviste = "11111220")
        {
            SendEvent(new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = napracovisti,
                PocetNaNovem = vporadku,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                KodPracoviste = pracoviste,
                StavNaradi = StavNaradi.VPoradku
            });
        }

        protected void SendPoskozene(string naradi, int pocet, int napracovisti, int poskozene, string pracoviste = "11111220")
        {
            SendEvent(new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = napracovisti,
                PocetNaNovem = poskozene,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" },
                KodPracoviste = pracoviste,
                StavNaradi = StavNaradi.NutnoOpravit
            });
        }

        protected void SendZnicene(string naradi, int pocet, int napracovisti, int znicene, string pracoviste = "11111220")
        {
            SendEvent(new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = napracovisti,
                PocetNaNovem = znicene,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" },
                KodPracoviste = pracoviste,
                StavNaradi = StavNaradi.Neopravitelne
            });
        }

        protected void SendOdeslano(string naradi, int pocet, int poskozene, int naobjednavce, string dodavatel = "D001", string objednavka = "294/2014")
        {
            SendEvent(new NecislovaneNaradiPredanoKOpraveEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = poskozene,
                PocetNaNovem = naobjednavce,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                TerminDodani = CurrentTime.Date.AddMonths(1),
                TypOpravy = TypOpravy.Oprava
            });
        }

        protected void SendOpravene(string naradi, int pocet, int naobjednavce, int vporadku, string dodavatel = "D001", string objednavka = "294/2014")
        {
            SendEvent(new NecislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = naobjednavce,
                PocetNaNovem = vporadku,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                TypOpravy = TypOpravy.Oprava,
                StavNaradi = StavNaradi.VPoradku,
                DodaciList = "D" + objednavka,
                Opraveno = StavNaradiPoOprave.Opraveno
            });
        }

        protected void SendNeopravitelne(string naradi, int pocet, int naobjednavce, int znicene, string dodavatel = "D001", string objednavka = "294/2014")
        {
            SendEvent(new NecislovaneNaradiPrijatoZOpravyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = naobjednavce,
                PocetNaNovem = znicene,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = dodavatel, Objednavka = objednavka },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" },
                TypOpravy = TypOpravy.Oprava,
                StavNaradi = StavNaradi.Neopravitelne,
                DodaciList = "D" + objednavka,
                Opraveno = StavNaradiPoOprave.Neopravitelne
            });
        }

        protected void SendSrotovane(string naradi, int pocet, int znicene)
        {
            SendEvent(new NecislovaneNaradiPredanoKeSesrotovaniEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Verze = _verze++,
                Pocet = pocet,
                PocetNaPredchozim = znicene,
                PocetNaNovem = 0,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSrotu }
            });
        }

        protected PrehledNaradiResponse _response;
        protected int _verze = 5;

        protected void ZiskatStranku(int stranka = 1)
        {
            _response = ReadProjection<PrehledNaradiRequest, PrehledNaradiResponse>(new PrehledNaradiRequest { Stranka = stranka });
        }

        protected override IEventProjection CreateProjection()
        {
            return new PrehledAktivnihoNaradiProjection(new PrehledAktivnihoNaradiRepository(_folder), _time);
        }

        protected override object CreateReader()
        {
            return new PrehledAktivnihoNaradiReader(new PrehledAktivnihoNaradiRepository(_folder), _time);
        }
    }
}
