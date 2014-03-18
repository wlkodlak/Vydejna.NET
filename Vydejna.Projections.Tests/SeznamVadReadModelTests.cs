using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Projections.SeznamVadReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class SeznamVadReadModelTests : ReadModelTestBase
    {
        private List<SeznamVadPolozka> _vady;

        [TestMethod]
        public void MozneCteniZNeexistujiciProjekce()
        {
            ZiskatSeznamVad();
        }

        [TestMethod]
        public void PridanaVadaMaVlastnostiZUdalosti()
        {
            var evnt = new DefinovanaVadaNaradiEvent
            {
                Kod = "5",
                Nazev = "Opotrebovano",
                Deaktivovana = false
            };
            SendEvent(evnt);
            ZiskatSeznamVad();
            var nalezeny = _vady.Single();
            Assert.AreEqual(evnt.Kod, nalezeny.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, nalezeny.Nazev, "Nazev");
            Assert.AreEqual(!evnt.Deaktivovana, nalezeny.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void NoviDodavateleJsouPridavaniDoSeznamu()
        {
            DefinovanaVada("7", "normalni opotrebeni");
            DefinovanaVada("8", "hazi, chveje");
            DefinovanaVada("9", "dobre");
            ZiskatSeznamVad();
            Assert.AreEqual(3, _vady.Count, "Pocet definovanych dodavatelu");
        }

        [TestMethod]
        public void ExistujiciVadaJeUpravena()
        {
            DefinovanaVada("7", "normalni opotrebeni");
            DefinovanaVada("8", "hazi, chveje");
            DefinovanaVada("9", "dobre");

            var evnt = new DefinovanaVadaNaradiEvent
            {
                Kod = "7",
                Nazev = "Opotrebovano",
                Deaktivovana = false
            };
            SendEvent(evnt);

            ZiskatSeznamVad();
            var nalezena = _vady.Single(d => d.Kod == "7");
            Assert.AreEqual(evnt.Kod, nalezena.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, nalezena.Nazev, "Nazev");
            Assert.AreEqual(!evnt.Deaktivovana, nalezena.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void DeaktivovaneVadyNejsouVeVystupnimSeznamu()
        {
            DefinovanaVada("7", "normalni opotrebeni");
            DefinovanaVada("8", "hazi, chveje");
            DefinovanaVada("9", "dobre");

            SendEvent(new DefinovanaVadaNaradiEvent { Kod = "9", Deaktivovana = true });
            ZiskatSeznamVad();

            var kodyVad = string.Join(", ", _vady.Select(d => d.Kod));
            Assert.AreEqual("7, 8", kodyVad);
        }

        [TestMethod]
        public void SeznamSerazenPodleKodu()
        {
            DefinovanaVada("9", "dobre");
            DefinovanaVada("7", "normalni opotrebeni");
            DefinovanaVada("8", "hazi, chveje");

            ZiskatSeznamVad();
            var kodyVad = string.Join(", ", _vady.Select(d => d.Kod));
            Assert.AreEqual("7, 8, 9", kodyVad);
        }

        private void DefinovanaVada(string kod, string nazev)
        {
            var evnt = new DefinovanaVadaNaradiEvent();
            evnt.Kod = kod;
            evnt.Nazev = nazev;
            evnt.Deaktivovana = string.IsNullOrEmpty(nazev);
            SendEvent(evnt);
        }

        private void ZiskatSeznamVad()
        {
            var response = ReadProjection<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>(new ZiskatSeznamVadRequest());
            _vady = response.Seznam;
        }

        protected override IEventProjection CreateProjection()
        {
            return new SeznamVadProjection(new SeznamVadRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new SeznamVadReader(new SeznamVadRepository(_folder), _executor, _time);
        }
    }
}
