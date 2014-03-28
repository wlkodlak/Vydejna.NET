using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using Vydejna.Contracts;
using Vydejna.Projections.SeznamNaradiReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class SeznamNaradiReadModelTests : ReadModelTestBase
    {
        private ZiskatSeznamNaradiResponse _response;

        [TestMethod]
        public void NeexistujiciDataDavajiPrazdnyVysledek()
        {
            ZiskatStranku();
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(0, _response.PocetStranek, "PocetStranek");
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.IsNotNull(_response.SeznamNaradi, "SeznamNaradi");
            Assert.AreEqual(0, _response.SeznamNaradi.Count, "SeznamNaradi.Count");
        }

        [TestMethod]
        public void DefinovaneNaradiMaVlastnostiPodleUdalosti()
        {
            var definiceNaradi = new DefinovanoNaradiEvent
            {
                NaradiId = Guid.NewGuid(),
                Verze = 1,
                Vykres = "888-809",
                Rozmer = "prum.36 č.5",
                Druh = "opěrka"
            };
            SendEvent(definiceNaradi);
            ZiskatStranku();
            Assert.AreNotEqual(0, _response.SeznamNaradi.Count, "SeznamNaradi.Count");
            var naradi = _response.SeznamNaradi[0];
            Assert.AreEqual(definiceNaradi.NaradiId, naradi.Id, "Id");
            Assert.AreEqual(definiceNaradi.Vykres, naradi.Vykres, "Vykres");
            Assert.AreEqual(definiceNaradi.Rozmer, naradi.Rozmer, "Rozmer");
            Assert.AreEqual(definiceNaradi.Druh, naradi.Druh, "Druh");
            Assert.AreEqual(true, naradi.Aktivni, "Aktivni");
        }

        /*
         * Neexistujici data davaji prazdny vysledek
         * Definovane naradi ma vlastnosti podle udalosti
         * Definice naradi pridava nove radky
         * Deaktivace naradi nemaze naradi ze seznamu
         * Aktivace naradi upravuje existujici
         * Seznam je serazen podle vykresu a rozmeru
         * Metadata pri velkem mnozstvi naradi
         * Druha stranka serazeneho seznamu
         */

        private void ZiskatStranku(int stranka = 1)
        {
            _response = ReadProjection<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(new ZiskatSeznamNaradiRequest(stranka));
        }

        protected override IEventProjection CreateProjection()
        {
            return new SeznamNaradiProjection(new SeznamNaradiRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new SeznamNaradiReader(new SeznamNaradiRepository(_folder), _executor, _time);
        }
    }
}
