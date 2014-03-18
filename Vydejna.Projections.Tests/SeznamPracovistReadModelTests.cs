using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Vydejna.Contracts;
using Vydejna.Projections.SeznamPracovistReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class SeznamPracovistReadModelTests : ReadModelTestBase
    {
        private ZiskatSeznamPracovistResponse _response;

        [TestMethod]
        public void NeexistujiciProjekceVraciPrazdnySeznam()
        {
            ZiskatStranku();
        }

        [TestMethod]
        public void PridanePracovisteMaVlastnostiPodleUdalosti()
        {
            var evnt = Definice("01221340", "Blumberg - 1 - broušení");
            SendEvent(evnt);
            ZiskatStranku();
            var pracoviste = GetSafe(() => _response.Seznam[0]);
            Assert.AreEqual(evnt.Kod, pracoviste.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, pracoviste.Nazev, "Nazev");
            Assert.AreEqual(evnt.Stredisko, pracoviste.Stredisko, "Stredisko");
            Assert.AreEqual(!evnt.Deaktivovano, pracoviste.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void JeMoznePridatVicePracovist()
        {
            SendEvent(Definice("01221340", "1 - broušení"));
            SendEvent(Definice("01291320", "2 - kalit"));
            SendEvent(Definice("01295330", "1 - navařování"));
            SendEvent(Definice("01314320", "4 - broušení"));
            SendEvent(Definice("01356320", "omílání"));
            ZiskatStranku();
            Assert.AreEqual(5, _response.Seznam.Count, "Pocet");
        }

        [TestMethod]
        public void ExistujiciPracovisteJeUpraveno()
        {
            SendEvent(Definice("01221340", "1 - broušení"));
            SendEvent(Definice("01291320", "2 - kalit"));
            SendEvent(Definice("01295330", "1 - navařování"));
            SendEvent(Definice("01314320", "4 - broušení"));
            SendEvent(Definice("01356320", "omílání"));

            var evnt = new DefinovanoPracovisteEvent
            {
                Kod = "01314320",
                Nazev = "Upraveny nazev",
                Stredisko = "111",
                Deaktivovano = false
            };
            SendEvent(evnt);

            ZiskatStranku();
            Assert.AreEqual(5, _response.Seznam.Count, "Pocet celkem");
            var pracoviste = GetSafe(() => _response.Seznam.Single(p => p.Kod == evnt.Kod));
            Assert.AreEqual(evnt.Kod, pracoviste.Kod, "Kod");
            Assert.AreEqual(evnt.Nazev, pracoviste.Nazev, "Nazev");
            Assert.AreEqual(evnt.Stredisko, pracoviste.Stredisko, "Stredisko");
            Assert.AreEqual(!evnt.Deaktivovano, pracoviste.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void DeaktiovanePracovisteNeniVSeznamu()
        {
            SendEvent(Definice("01221340", "1 - broušení"));
            SendEvent(Definice("01291320", "2 - kalit"));
            SendEvent(Definice("01295330", "1 - navařování"));
            SendEvent(Definice("01314320", "4 - broušení"));
            SendEvent(Definice("01356320", "omílání"));

            var evnt = new DefinovanoPracovisteEvent
            {
                Kod = "01295330",
                Deaktivovano = true
            };
            SendEvent(evnt);

            ZiskatStranku();
            var kody = GetSafe(() => _response.Seznam.Select(p => p.Kod).ToList());
            var ocekavane = "01221340, 01291320, 01314320, 01356320";
            var skutecne = string.Join(", ", kody.OrderBy(k => k));
            Assert.AreEqual(ocekavane, skutecne);
        }

        [TestMethod]
        public void PracovisteJsouSerazenaPodleKodu()
        {
            SendEvent(Definice("01314320", "4 - broušení"));
            SendEvent(Definice("01295330", "1 - navařování"));
            SendEvent(Definice("01221340", "1 - broušení"));
            SendEvent(Definice("01356320", "omílání"));
            SendEvent(Definice("01291320", "2 - kalit"));

            ZiskatStranku();
            var kody = GetSafe(() => _response.Seznam.Select(p => p.Kod).ToList());
            var ocekavane = "01221340, 01291320, 01295330, 01314320, 01356320";
            var skutecne = string.Join(", ", kody);
            Assert.AreEqual(ocekavane, skutecne);
        }

        [TestMethod]
        public void MetadataPriVelkemCelkovemPoctu()
        {
            GenerovatHodnePracovist(374);
            ZiskatStranku();

            Assert.AreEqual(374, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, _response.PocetStranek, "PocetStranek");
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.AreEqual(100, _response.Seznam.Count, "Seznam.Count");
        }

        [TestMethod]
        public void ObsahDruheStranky()
        {
            var cisla = GenerovatCislaHodnePracovist(400);
            GenerovatHodnePracovist(cisla);
            ZiskatStranku(2);

            var ocekavane = cisla.Select(c => c.ToString("00000")).OrderBy(k => k).Skip(100).Take(100).ToList();
            var skutecne = _response.Seznam.Select(p => p.Kod.Substring(0, 5)).ToList();
            Assert.AreEqual(ocekavane.Count, skutecne.Count, "Pocet");
            Assert.AreEqual(ocekavane[0], skutecne[0], "[0]");
            Assert.AreEqual(ocekavane[99], skutecne[99], "[99]");
            CollectionAssert.AreEqual(ocekavane, skutecne);
        }

        private List<int> GenerovatCislaHodnePracovist(int pocet)
        {
            var seznam = new List<int>(pocet);
            var multiplier = pocet / 16 + 1;
            for (int i = 0; i < pocet; i++)
            {
                var cislo = (i % 16) * multiplier + (i / 16);
                seznam.Add(cislo);
            }
            return seznam;
        }

        private void GenerovatHodnePracovist(List<int> cisla)
        {
            foreach (var cislo in cisla)
            {
                var kod = cislo.ToString("00000") + "310";
                var nazev = "Pracoviste #" + cislo.ToString();
                SendEvent(Definice(kod, nazev));
            }
        }

        private void GenerovatHodnePracovist(int pocet)
        {
            GenerovatHodnePracovist(GenerovatCislaHodnePracovist(pocet));
        }

        private void ZiskatStranku(int stranka = 1)
        {
            _response = ReadProjection<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>(new ZiskatSeznamPracovistRequest { Stranka = stranka });
        }

        private DefinovanoPracovisteEvent Definice(string kod, string nazev)
        {
            return new DefinovanoPracovisteEvent
            {
                Kod = kod,
                Deaktivovano = string.IsNullOrEmpty(nazev),
                Nazev = nazev,
                Stredisko = string.IsNullOrEmpty(nazev) ? null : kod.Substring(kod.Length - 3, 3)
            };
        }

        private T GetSafe<T>(Expression<Func<T>> func)
        {
            try
            {
                return func.Compile()();
            }
            catch (Exception ex)
            {
                Assert.Fail("When trying to get {0}: {1}", func.ToString(), ex.Message);
                return default(T);
            }
        }

        protected override IEventProjection CreateProjection()
        {
            return new SeznamPracovistProjection(new SeznamPracovistRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new SeznamPracovistReader(new SeznamPracovistRepository(_folder), _executor, _time);
        }
    }
}
