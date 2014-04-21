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
            ZiskatNaradi();
        }

        [TestMethod]
        public void NaradiIdPodleRequestu()
        {
            Assert.AreEqual(_build.NaradiId, _response.NaradiId);
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
            _build.Definovano("4839-3341-0", "55x22", "kotouc");
        }

        protected override void When()
        {
            ZiskatNaradi();
        }

        [TestMethod]
        public void InformaceONaradiPrevzatyZDefinice()
        {
            Assert.AreEqual(_build.NaradiId, _response.NaradiId);
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
    public class DetailNaradiReadModelTest_ZmenyNaSklade : DetailNaradiReadModelPresunTestBase
    {
        protected override void Given()
        {
            base.Given();
            _build.ZmenaNaSklade().Zmena(4).Send();
            _build.ZmenaNaSklade().Zmena(3).Send();
        }

        [TestMethod]
        public void NovyPocetNaSklade()
        {
            Assert.AreEqual(7, _response.NaSklade);
        }
    }

    [TestClass]
    public class DetailNaradiReadModelTest_PrijatoNaVydejnu : DetailNaradiReadModelPresunTestBase
    {
        protected override void Given()
        {
            base.Given();
            _build.Prijmout().Necislovane(5).Send();
            _build.Prijmout().Cislovane(3).Send();
            _build.Prijmout().Cislovane(8).Send();
            _build.Prijmout().Necislovane(2).Send();
        }

        [TestMethod]
        public void PocetCelkem()
        {
            AssertPocty(_response.PoctyCelkem, 9, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyNecislovane()
        {
            AssertPocty(_response.PoctyNecislovane, 7, 0, 0, 0, 0);
        }

        [TestMethod]
        public void PoctyCislovane()
        {
            AssertPocty(_response.PoctyCislovane, 2, 0, 0, 0, 0);
        }

        [TestMethod]
        public void SeznamCislovanychObsahujeDveNaradi()
        {
            Assert.AreEqual(2, _response.Cislovane.Count);
        }

        [TestMethod]
        public void CislovaneNaradi_3()
        {
            var cislovane = _response.Cislovane.FirstOrDefault(c => c.CisloNaradi == 3);
            Assert.IsNotNull(cislovane);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, cislovane.ZakladUmisteni);
            Assert.IsNotNull(cislovane.NaVydejne);
            Assert.IsNull(cislovane.VeVyrobe);
            Assert.IsNull(cislovane.VOprave);
            Assert.AreEqual("", cislovane.NaVydejne.KodVady);
            Assert.AreEqual("", cislovane.NaVydejne.NazevVady);
            Assert.AreEqual(StavNaradi.VPoradku, cislovane.NaVydejne.StavNaradi);
        }

        [TestMethod]
        public void CislovaneNaradi_8()
        {
            var cislovane = _response.Cislovane.FirstOrDefault(c => c.CisloNaradi == 8);
            Assert.IsNotNull(cislovane);
            Assert.AreEqual(ZakladUmisteni.NaVydejne, cislovane.ZakladUmisteni);
            Assert.IsNotNull(cislovane.NaVydejne);
            Assert.IsNull(cislovane.VeVyrobe);
            Assert.IsNull(cislovane.VOprave);
            Assert.AreEqual("", cislovane.NaVydejne.KodVady);
            Assert.AreEqual("", cislovane.NaVydejne.NazevVady);
            Assert.AreEqual(StavNaradi.VPoradku, cislovane.NaVydejne.StavNaradi);
        }

        [TestMethod]
        public void SeznamNecislovanych()
        {
            var necislovane = _response.Necislovane.FirstOrDefault();
            Assert.AreEqual(ZakladUmisteni.NaVydejne, necislovane.ZakladUmisteni);
            Assert.IsNotNull(necislovane.NaVydejne);
            Assert.IsNull(necislovane.VeVyrobe);
            Assert.IsNull(necislovane.VOprave);
            Assert.AreEqual(7, necislovane.Pocet);
        }
    }

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

    public class DetailNaradiReadModelPresunTestBase : DetailNaradiReadModelTestBase
    {
        protected override void Given()
        {
            _build.Definovano("4839-3341-0", "55x22", "zvedak");
        }

        protected override void When()
        {
            ZiskatNaradi();
        }
    }

    public class DetailNaradiReadModelTestBase : ReadModelTestBase
    {
        protected DetailNaradiResponse _response;
        protected NaradiTestEventBuilder _build;
        protected ProjectionTestEventBuilder _master;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _master = new ProjectionTestEventBuilder(this);
            _build = _master.Naradi("A");
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

        protected void ZiskatNaradi()
        {
            _response = ReadProjection<DetailNaradiRequest, DetailNaradiResponse>(
                new DetailNaradiRequest { NaradiId = _build.NaradiId });
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
