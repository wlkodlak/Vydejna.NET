using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.NaradiObecneTesty
{
    [TestClass]
    public class UmisteniNaradiTest
    {
        [TestMethod]
        public void VeSkladu_Equality()
        {
            var umisteni = UmisteniNaradi.VeSkladu();
            Assert.AreEqual(UmisteniNaradi.VeSkladu(), umisteni);
            Assert.AreNotEqual(UmisteniNaradi.NaPracovisti("48397330"), umisteni);
        }
        [TestMethod]
        public void VeSkladu_Dto()
        {
            TestDto("Ve skladu",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu },
                UmisteniNaradi.VeSkladu());
        }

        [TestMethod]
        public void VeSrotu_Equality()
        {
            var umisteni = UmisteniNaradi.VeSrotu();
            Assert.AreEqual(UmisteniNaradi.VeSrotu(), umisteni);
            Assert.AreNotEqual(UmisteniNaradi.NaPracovisti("48397330"), umisteni);
        }
        [TestMethod]
        public void VeSrotu_Dto()
        {
            TestDto("Ve srotu",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSrotu },
                UmisteniNaradi.VeSrotu());
        }

        [TestMethod]
        public void NaVydejne_Equality()
        {
            var vporadku = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku);
            var proopravu = UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit);
            var prosrot = UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne);
            var vesrotu = UmisteniNaradi.VeSrotu();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku), vporadku);
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit), proopravu);
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne), prosrot);
            Assert.AreNotEqual(vporadku, proopravu);
            Assert.AreNotEqual(prosrot, vesrotu);
        }
        [TestMethod]
        public void NaVydejne_Dto()
        {
            TestDto("V poradku",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                UmisteniNaradi.NaVydejne(StavNaradi.VPoradku));
            TestDto("Pro srot",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" },
                UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne));
        }

        [TestMethod]
        public void NaPracovisti_Equality()
        {
            var spotrebaA = UmisteniNaradi.Spotrebovano("49873330");
            var pracovisteA = UmisteniNaradi.NaPracovisti("49873330");
            var pracovisteB = UmisteniNaradi.NaPracovisti("49873330");
            var pracovisteC = UmisteniNaradi.NaPracovisti("49098320");
            var srot = UmisteniNaradi.VeSrotu();
            Assert.AreEqual(pracovisteA, pracovisteB);
            Assert.AreNotEqual(spotrebaA, pracovisteA);
            Assert.AreNotEqual(pracovisteC, pracovisteA);
            Assert.AreNotEqual(pracovisteB, srot);
        }
        [TestMethod]
        public void NaPracovisti_Dto()
        {
            TestDto("A",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = "83922330" },
                UmisteniNaradi.NaPracovisti("83922330"));
        }

        [TestMethod]
        public void Spotrebovano_Equality()
        {
            var normalneA = UmisteniNaradi.NaPracovisti("49873330");
            var pracovisteA = UmisteniNaradi.Spotrebovano("49873330");
            var pracovisteB = UmisteniNaradi.Spotrebovano("49873330");
            var pracovisteC = UmisteniNaradi.Spotrebovano("49098320");
            var srot = UmisteniNaradi.VeSrotu();
            Assert.AreEqual(pracovisteA, pracovisteB);
            Assert.AreNotEqual(normalneA, pracovisteA);
            Assert.AreNotEqual(pracovisteC, pracovisteA);
            Assert.AreNotEqual(pracovisteB, srot);
        }
        [TestMethod]
        public void Spotrebovano_Dto()
        {
            TestDto("A",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.Spotrebovano, Pracoviste = "89742132" },
                UmisteniNaradi.Spotrebovano("89742132"));
        }

        [TestMethod]
        public void NaOprave_Equality()
        {
            var opravaA1 = UmisteniNaradi.NaOprave(TypOpravy.Oprava, "A", "1001");
            var opravaA1b = UmisteniNaradi.NaOprave(TypOpravy.Oprava, "A", "1001");
            var reklamaceA = UmisteniNaradi.NaOprave(TypOpravy.Reklamace, "A", "1001");
            var reklamaceAb = UmisteniNaradi.NaOprave(TypOpravy.Reklamace, "A", "1001");
            var opravaB = UmisteniNaradi.NaOprave(TypOpravy.Oprava, "B", "1001");
            var opravaA2 = UmisteniNaradi.NaOprave(TypOpravy.Oprava, "A", "1002");
            Assert.AreEqual(opravaA1, opravaA1b);
            Assert.AreEqual(reklamaceA, reklamaceAb);
            Assert.AreNotEqual(opravaA1, reklamaceA);
            Assert.AreNotEqual(opravaA1, opravaB);
            Assert.AreNotEqual(opravaA1, opravaA2);
        }
        [TestMethod]
        public void NaOprave_Dto()
        {
            TestDto("OpravaA",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava", Dodavatel = "A", Objednavka = "1001" },
                UmisteniNaradi.NaOprave(TypOpravy.Oprava, "A", "1001"));
            TestDto("ReklamaceB",
                new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Reklamace", Dodavatel = "B", Objednavka = "293" },
                UmisteniNaradi.NaOprave(TypOpravy.Reklamace, "B", "293"));
        }

        private void TestDto(string nazevTestu, UmisteniNaradiDto dto, UmisteniNaradi umisteni)
        {
            var dekodovano = UmisteniNaradi.Dto(dto);
            var zakodovano = umisteni.Dto();
            Assert.AreEqual(umisteni, dekodovano, "Dekodovani {0}", nazevTestu);
            Assert.AreEqual(dto, zakodovano, "Zakodovani {0}", nazevTestu);
        }
    }
}
