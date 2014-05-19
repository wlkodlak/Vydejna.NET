using System;
using System.Collections.Generic;
using System.Linq;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Vydejna.Contracts;
using Vydejna.Projections.NaradiNaPracovistiReadModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class NaradiNaPracovistiReadModelTest_NeexistujiciPracoviste : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            ZiskatPracoviste("84752240");
        }

        [TestMethod]
        public void PracovisteNeexistuje()
        {
            Assert.AreEqual(false, _response.PracovisteExistuje, "PracovisteExistuje");
        }

        [TestMethod]
        public void NulovePoctyNaradi()
        {
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam, "Seznam");
            Assert.AreEqual(0, _response.Seznam.Count, "Seznam.Count");
        }

        [TestMethod]
        public void DoplnenyInformaceOPracovisti()
        {
            Assert.IsNotNull(_response.Pracoviste, "Pracoviste");
            Assert.AreEqual("84752240", _response.Pracoviste.Kod, "Pracoviste.Kod");
            Assert.AreEqual("", _response.Pracoviste.Nazev, "Pracoviste.Nazev");
            Assert.AreEqual("", _response.Pracoviste.Stredisko, "Pracoviste.Stredisko");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_ExistujiciPracovisteBezVydeju : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void UdajeOPracovistiDoplnenyZDefinice()
        {
            Assert.IsNotNull(_response.Pracoviste, "Pracoviste");
            Assert.AreEqual("24710330", _response.Pracoviste.Kod, "Pracoviste.Kod");
            Assert.AreEqual("Svarovani", _response.Pracoviste.Nazev, "Pracoviste.Nazev");
            Assert.AreEqual("330", _response.Pracoviste.Stredisko, "Pracoviste.Stredisko");
        }

        [TestMethod]
        public void NulovePocty()
        {
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(true, _response.PracovisteExistuje, "PracovisteExistuje");
            Assert.IsNotNull(_response.Seznam, "Seznam");
            Assert.AreEqual(0, _response.Seznam.Count, "Seznam.Count");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_VydanoNoveNaradi : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            SendDefinovanoNaradi("0001", "483-2284", "prum. 15", "kotouc");
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            SendVydano("0001", "24710330", 5, 5);
            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void DefinovaneNaradiJeVSeznamu()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.OrderBy(n => n.NaradiId).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(NaradiId("0001"), naradi.NaradiId, "NaradiId");
            Assert.AreEqual("483-2284", naradi.Vykres, "Vykres");
            Assert.AreEqual("prum. 15", naradi.Rozmer, "Rozmer");
            Assert.AreEqual("kotouc", naradi.Druh, "Druh");
        }

        [TestMethod]
        public void CelkovePocty()
        {
            Assert.AreEqual(5, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam);
            Assert.AreEqual(1, _response.Seznam.Count, "Count");
        }

        [TestMethod]
        public void PoctyNaradi()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0001")).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(CurrentTime, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(5, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(5, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual(0, naradi.SeznamCislovanych.Count, "SeznamCislovanych");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_VydanoViceNovychNaradi : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            SendDefinovanoNaradi("0001", "483-2284", "prum. 15", "kotouc");
            SendDefinovanoNaradi("0002", "222-1744", "prum. 20", "kotouc");
            SendDefinovanoNaradi("0003", "4-227-84", "20x15x10", "drzak");
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            SendVydano("0001", "24710330", 5, 5);
            SendVydanoCislovane("0001", "24710330", 3);
            SendVydano("0002", "24710330", 4, 4);
            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void DefinovaneNaradiJeVSeznamu()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.OrderBy(n => n.NaradiId).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(NaradiId("0001"), naradi.NaradiId, "NaradiId");
            Assert.AreEqual("483-2284", naradi.Vykres, "Vykres");
            Assert.AreEqual("prum. 15", naradi.Rozmer, "Rozmer");
            Assert.AreEqual("kotouc", naradi.Druh, "Druh");
        }

        [TestMethod]
        public void CelkovePocty()
        {
            Assert.AreEqual(10, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam);
            Assert.AreEqual(2, _response.Seznam.Count, "Count");
        }

        [TestMethod]
        public void PoctyNaradiA()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0001")).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(CurrentTime, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(6, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(5, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual(1, naradi.SeznamCislovanych.Count, "SeznamCislovanych");
        }

        [TestMethod]
        public void PoctyNaradiB()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0002")).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(CurrentTime, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(4, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual(0, naradi.SeznamCislovanych.Count, "SeznamCislovanych");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_NeuplneVraceneNaradi : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            SendDefinovanoNaradi("0001", "483-2284", "prum. 15", "kotouc");
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            SendVydano("0001", "24710330", 5, 5);
            SendVraceno("0001", "24710330", 2, 3);
            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void NaradiJeVSeznamu()
        {
            Assert.IsNotNull(_response.Seznam);
            Assert.AreEqual(1, _response.Seznam.Count, "Count");
            var naradi = _response.Seznam.OrderBy(n => n.NaradiId).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(NaradiId("0001"), naradi.NaradiId, "NaradiId");
        }

        [TestMethod]
        public void PoctyNaradi()
        {
            Assert.AreEqual(3, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0001")).FirstOrDefault();
            Assert.AreEqual(CurrentTime, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(3, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(3, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual(0, naradi.SeznamCislovanych.Count, "SeznamCislovanych");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_UplneVraceneNaradi : NaradiNaPracovistiReadModelTestBase
    {
        protected override void When()
        {
            SendDefinovanoNaradi("0001", "483-2284", "prum. 15", "kotouc");
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            SendVydano("0001", "24710330", 5, 5);
            SendVraceno("0001", "24710330", 2, 3);
            SendVraceno("0001", "24710330", 3, 0);
            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void NaradiZmizeloZeSeznamu()
        {
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam);
            Assert.AreEqual(0, _response.Seznam.Count, "Count");
        }
    }

    [TestClass]
    public class NaradiNaPracovistiReadModelTest_KomplexniPripad : NaradiNaPracovistiReadModelTestBase
    {
        private DateTime _casA, _casB, _casC;

        protected override void When()
        {
            _casA = new DateTime(2014, 3, 20, 15, 33, 20);
            _casB = new DateTime(2014, 3, 21, 8, 15, 0);
            _casC = new DateTime(2014, 3, 21, 10, 21, 40);

            _time.SetTime(_casA);
            SendDefinovanoNaradi("0001", "483-2284", "prum. 15", "kotouc");
            SendDefinovanoNaradi("0002", "222-1744", "prum. 20", "kotouc");
            SendDefinovanoNaradi("0003", "4-227-84", "20x15x10", "drzak");
            SendDefinovanoPracoviste("24710330", "Svarovani", "330");
            SendDefinovanoPracoviste("24722440", "Svarovani 2", "440");

            SendVydano("0001", "24710330", 5, 5);
            SendVydanoCislovane("0001", "24710330", 3);
            SendVydano("0001", "24722440", 3, 3);
            SendVydano("0003", "24710330", 1, 1);
            _time.SetTime(_casB);
            SendVydano("0002", "24710330", 4, 4);
            SendVraceno("0003", "24710330", 1, 0);
            SendVraceno("0001", "24710330", 2, 3);
            SendVydanoCislovane("0001", "24710330", 7);
            SendVydano("0001", "24710330", 1, 4);
            _time.SetTime(_casC);
            SendVydano("0002", "24710330", 2, 6);
            SendVracenoCislovane("0001", "24710330", 3, "9");

            ZiskatPracoviste("24710330");
        }

        [TestMethod]
        public void CelkovyPocet()
        {
            Assert.AreEqual(11, _response.PocetCelkem, "PocetCelkem");
            Assert.IsNotNull(_response.Seznam);
            Assert.AreEqual(2, _response.Seznam.Count, "Count");
        }

        [TestMethod]
        public void PoctyNaradiA()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0001")).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(_casB, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(5, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual("7", string.Join(", ", naradi.SeznamCislovanych), "SeznamCislovanych");
        }

        [TestMethod]
        public void PoctyNaradiB()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0002")).FirstOrDefault();
            Assert.IsNotNull(naradi);
            Assert.AreEqual(_casC, naradi.DatumPoslednihoVydeje, "DatumPoslednihoVydeje");
            Assert.AreEqual(6, naradi.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(6, naradi.PocetNecislovanych, "PocetNecislovanych");
            Assert.IsNotNull(naradi.SeznamCislovanych, "SeznamCislovanych");
            Assert.AreEqual("", string.Join(", ", naradi.SeznamCislovanych), "SeznamCislovanych");
        }

        [TestMethod]
        public void ZmizeloNaradiC()
        {
            Assert.IsNotNull(_response.Seznam);
            var naradi = _response.Seznam.Where(n => n.NaradiId == NaradiId("0003")).FirstOrDefault();
            Assert.IsNull(naradi);
        }
    }

    public class NaradiNaPracovistiReadModelTestBase : ReadModelTestBase
    {
        protected ZiskatNaradiNaPracovistiResponse _response;

        protected void ZiskatPracoviste(string pracoviste)
        {
            _response = ReadProjection<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>(
                new ZiskatNaradiNaPracovistiRequest { KodPracoviste = pracoviste });
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

        protected void SendVydano(string naradi, string pracoviste, int pocetVydany, int pocetCelkem = 0)
        {
            pocetCelkem = Math.Max(pocetCelkem, pocetVydany);
            SendEvent(new NecislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Pocet = pocetVydany,
                PocetNaNovem = pocetCelkem,
                PocetNaPredchozim = 0,
                KodPracoviste = pracoviste,
                Verze = 8,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
            });
        }

        protected void SendVraceno(string naradi, string pracoviste, int pocetVraceny, int pocetZbyvajici = 0)
        {
            SendEvent(new NecislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                Pocet = pocetVraceny,
                PocetNaNovem = pocetVraceny,
                PocetNaPredchozim = pocetZbyvajici,
                KodPracoviste = pracoviste,
                Verze = 9,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                StavNaradi = StavNaradi.VPoradku,
            });
        }

        protected void SendVydanoCislovane(string naradi, string pracoviste, int cisloNaradi)
        {
            SendEvent(new CislovaneNaradiVydanoDoVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                CisloNaradi = cisloNaradi,
                KodPracoviste = pracoviste,
                Verze = 8,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
            });
        }

        protected void SendVracenoCislovane(string naradi, string pracoviste, int cisloNaradi, string vada)
        {
            SendEvent(new CislovaneNaradiPrijatoZVyrobyEvent
            {
                EventId = Guid.NewGuid(),
                Datum = CurrentTime,
                NaradiId = NaradiId(naradi),
                CisloNaradi = cisloNaradi,
                KodPracoviste = pracoviste,
                Verze = 9,
                PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe, Pracoviste = pracoviste },
                NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" },
                KodVady = vada,
                StavNaradi = StavNaradi.VPoradku
            });
        }

        protected override IEventProjection CreateProjection()
        {
            return new NaradiNaPracovistiProjection(new NaradiNaPracovistiRepository(_folder), _time);
        }

        protected override object CreateReader()
        {
            return new NaradiNaPracovistiReader(new NaradiNaPracovistiRepository(_folder), _time);
        }
    }
}
