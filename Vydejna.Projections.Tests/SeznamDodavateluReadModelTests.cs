using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Projections.SeznamDodavateluReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class SeznamDodavateluReadModelTests : ReadModelTestBase
    {
        private List<InformaceODodavateli> _dodavatele;

        [TestMethod]
        public void MozneCteniZNeexistujiciProjekce()
        {
            ZiskatSeznamDodavatelu();
        }

        [TestMethod]
        public void PridanyDodavatelMaVlastnostiZUdalosti()
        {
            var evnt = new DefinovanDodavatelEvent
            {
                Kod = "C8475",
                Nazev = "Dobry Dodavatel, s.r.o.",
                Deaktivovan = false,
                Ico = "84772514",
                Dic = "187-84772514",
                Adresa = new[] { "Dlouha 472/14", "602 00  Brno" }
            };
            SendEvent(evnt);
            ZiskatSeznamDodavatelu();
            var nalezeny = _dodavatele.Single();
            Assert.AreEqual(evnt.Kod, nalezeny.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, nalezeny.Nazev, "Nazev");
            Assert.AreEqual(evnt.Ico, nalezeny.Ico, "Ico");
            Assert.AreEqual(evnt.Dic, nalezeny.Dic, "Dic");
            Assert.AreEqual(!evnt.Deaktivovan, nalezeny.Aktivni, "Aktivni");
            Assert.AreEqual(string.Join("\r\n", evnt.Adresa), string.Join("\r\n", nalezeny.Adresa), "Adresa");
        }

        [TestMethod]
        public void NoviDodavateleJsouPridavaniDoSeznamu()
        {
            DefinovanDodavatel("K875", "Dodavatel 1");
            DefinovanDodavatel("K821", "Dodavatel 2");
            DefinovanDodavatel("K267", "Dodavatel 3");
            DefinovanDodavatel("K371", "Dodavatel 4");
            DefinovanDodavatel("K277", "Dodavatel 5");
            ZiskatSeznamDodavatelu();
            Assert.AreEqual(5, _dodavatele.Count, "Pocet definovanych dodavatelu");
        }

        [TestMethod]
        public void ExistujiciDodavatelJeUpraven()
        {
            DefinovanDodavatel("K875", "Dodavatel 1");
            DefinovanDodavatel("K821", "Dodavatel 2");
            DefinovanDodavatel("K267", "Dodavatel 3");
            DefinovanDodavatel("K371", "Dodavatel 4");
            DefinovanDodavatel("K277", "Dodavatel 5");

            var evnt = new DefinovanDodavatelEvent
            {
                Kod = "K267",
                Nazev = "Dobry Dodavatel, s.r.o.",
                Deaktivovan = false,
                Ico = "84772514",
                Dic = "187-84772514",
                Adresa = new[] { "Dlouha 472/14", "602 00  Brno" }
            };
            SendEvent(evnt);

            ZiskatSeznamDodavatelu();
            var nalezeny = _dodavatele.Single(d => d.Kod == "K267");
            Assert.AreEqual(evnt.Kod, nalezeny.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, nalezeny.Nazev, "Nazev");
            Assert.AreEqual(evnt.Ico, nalezeny.Ico, "Ico");
            Assert.AreEqual(evnt.Dic, nalezeny.Dic, "Dic");
            Assert.AreEqual(!evnt.Deaktivovan, nalezeny.Aktivni, "Aktivni");
            Assert.AreEqual(string.Join("\r\n", evnt.Adresa), string.Join("\r\n", nalezeny.Adresa), "Adresa");
        }

        [TestMethod]
        public void DeaktivovaniDodavateleNejsouVeVystupnimSeznamu()
        {
            DefinovanDodavatel("K875", "Dodavatel 1");
            DefinovanDodavatel("K821", "Dodavatel 2");
            DefinovanDodavatel("K267", "Dodavatel 3");
            DefinovanDodavatel("K371", "Dodavatel 4");
            DefinovanDodavatel("K277", "Dodavatel 5");

            SendEvent(new DefinovanDodavatelEvent { Kod = "K371", Deaktivovan = true });
            ZiskatSeznamDodavatelu();

            var kodyDodavatelu = string.Join(", ", _dodavatele.Select(d => d.Kod));
            Assert.AreEqual("K875, K821, K267, K277", kodyDodavatelu);
        }

        [TestMethod]
        public void SeznamSerazenPodleNazvu()
        {
            DefinovanDodavatel("K267", "Dodavatel 3");
            DefinovanDodavatel("K277", "Dodavatel 5");
            DefinovanDodavatel("K875", "Dodavatel 1");
            DefinovanDodavatel("K371", "Dodavatel 4");
            DefinovanDodavatel("K821", "Dodavatel 2");

            ZiskatSeznamDodavatelu();
            var kodyDodavatelu = string.Join(", ", _dodavatele.Select(d => d.Kod));
            Assert.AreEqual("K875, K821, K267, K371, K277", kodyDodavatelu);
        }

        private void DefinovanDodavatel(string kod, string nazev)
        {
            var evnt = new DefinovanDodavatelEvent();
            evnt.Kod = kod;
            evnt.Nazev = nazev;
            evnt.Deaktivovan = string.IsNullOrEmpty(nazev);
            evnt.Adresa = evnt.Deaktivovan ? null : new[] { "Adresa " + nazev };
            evnt.Ico = "ICO" + kod;
            evnt.Dic = "DIC" + kod;
            SendEvent(evnt);
        }

        private void ZiskatSeznamDodavatelu()
        {
            var response = ReadProjection<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>(new ZiskatSeznamDodavateluRequest());
            _dodavatele = response.Seznam;
        }

        protected override IEventProjection CreateProjection()
        {
            return new SeznamDodavateluProjection(new SeznamDodavateluRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new SeznamDodavateluReader(new SeznamDodavateluRepository(_folder), _executor, _time);
        }
    }

}
