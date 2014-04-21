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
            _build.ZmenaNaSklade(4, 4);
            _build.ZmenaNaSklade(3, 7);
        }

        [TestMethod]
        public void NovyPocetNaSklade()
        {
            Assert.AreEqual(7, _response.NaSklade);
        }
    }

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
