using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Projections.DetailNaradiReadModel;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class DetailNaradiReadModelTest_NeexistujiciNaradi : DetailNaradiReadModelTestBase
    {
        protected override void When()
        {
            ZiskatNaradi("A");
        }

        [TestMethod]
        public void NaradiIdPodleRequestu()
        {
            Assert.AreEqual(NaradiId("A"), _response.NaradiId);
        }

        [TestMethod]
        public void InformaceONaradiJsouPrazdneRetezce()
        {
            Assert.AreEqual("", _response.Vykres);
            Assert.AreEqual("", _response.Rozmer);
            Assert.AreEqual("", _response.Druh);
            Assert.AreEqual(false, _response.Aktivni);
        }

        [TestMethod]
        public void PoctyNaradiNulove()
        {
            Assert.AreEqual(0, _response.NaSklade);
            AssertPocty(_response.PoctyCelkem, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyCislovane, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyNecislovane, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PrazdneSeznamyCislovanychANecislovanychKusu()
        {
            Assert.IsNotNull(_response.Necislovane);
            Assert.AreEqual(0, _response.Necislovane.Count);
            Assert.IsNotNull(_response.Cislovane);
            Assert.AreEqual(0, _response.Cislovane.Count);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_DefinovaneNaradi : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("A", "4839-3341-0", "55x22", "kotouc");
        }

        protected override void When()
        {
            ZiskatNaradi("A");
        }

        [TestMethod]
        public void InformaceONaradiPrevzatyZDefinice()
        {
            Assert.AreEqual(NaradiId("A"), _response.NaradiId);
            Assert.AreEqual("4839-3341-0", _response.Vykres);
            Assert.AreEqual("55x22", _response.Rozmer);
            Assert.AreEqual("kotouc", _response.Druh);
            Assert.AreEqual(true, _response.Aktivni);
        }

        [TestMethod]
        public void PoctyNaradiNulove()
        {
            Assert.AreEqual(0, _response.NaSklade);
            AssertPocty(_response.PoctyCelkem, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyCislovane, 0, 0, 0, 0, 0);
            AssertPocty(_response.PoctyNecislovane, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PrazdneSeznamyCislovanychANecislovanychKusu()
        {
            Assert.IsNotNull(_response.Necislovane);
            Assert.AreEqual(0, _response.Necislovane.Count);
            Assert.IsNotNull(_response.Cislovane);
            Assert.AreEqual(0, _response.Cislovane.Count);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_NaradiPrijato : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            base.Given();
        }

    }

    public class DetailNaradiReadModelPresunTestBase : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            SendDefinovanoNaradi("A", "4839-3341-0", "55x22", "zvedak");
            SendDefinovanoNaradi("B", "382-1432", "prum. 15", "kotouc");
        }

        protected override void When()
        {
            ZiskatNaradi("A");
        }
    }

    public class DetailNaradiReadModelTestBase : ReadModelTestBase
    {
        /*
         * Prijem necislovaneho naradi na vydejnu
         * - zvysuji se pocty celkove a necislovane
         * - do seznamu necislovanych pribude radek s umistenim, celkovym poctem a detailem na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem cislovaneho naradi na vydejnu
         * - zvysuji se pocty celkove a cislovane
         * - do seznamu cislovanych pribude radek s cislem naradi, umistenim a detailem na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Vydej do vyroby
         * - upravi se pocty
         * - odstrani se puvodni detail, vytvori se detail ve vyrobe podle pracoviste
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem poskozeneho
         * - upravi se pocty
         * - detail bude pro opravu podle objednavky vcetne dodavatele
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Navrat opraveneho naradi na vydejnu
         * - pocty
         * - detail na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem zniceneho naradi
         * - pocty
         * - detail na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Odeslani do srotu
         * - pocty
         * - cislovane naradi se odstranuje ze seznamu
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         */
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


        protected DetailNaradiResponse _response;
        protected int _verze;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _verze = 3;
        }

        protected static Guid NaradiId(string naradi)
        {
            switch (naradi)
            {
                case "A": return new Guid("0000000A-3849-55b1-c23d-18be1932bb11");
                case "B": return new Guid("0000000B-3849-55b1-c23d-18be1932bb11");
                case "C": return new Guid("0000000C-3849-55b1-c23d-18be1932bb11");
                case "D": return new Guid("0000000D-3849-55b1-c23d-18be1932bb11");
                default: return new Guid("0000" + naradi + "-3849-55b1-c23d-18be1932bb11");
            }
        }

        protected void AssertPocty(DetailNaradiPocty pocty, int vporadku, int vevyrobe, int poskozene, int voprave, int znicene)
        {
            Assert.IsNotNull(pocty);
            Assert.AreEqual(vporadku, pocty.VPoradku);
            Assert.AreEqual(vevyrobe, pocty.VeVyrobe);
            Assert.AreEqual(poskozene, pocty.Poskozene);
            Assert.AreEqual(voprave, pocty.VOprave);
            Assert.AreEqual(znicene, pocty.Znicene);
        }

        protected void ZiskatNaradi(string naradi)
        {
            _response = ReadProjection<DetailNaradiRequest, DetailNaradiResponse>(
                new DetailNaradiRequest { NaradiId = NaradiId(naradi) });
        }

        protected override IEventProjection CreateProjection()
        {
            return new DetailNaradiProjection(new DetailNaradiRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new DetailNaradiReader(new DetailNaradiRepository(_folder), _executor, _time);
        }
    }
}
