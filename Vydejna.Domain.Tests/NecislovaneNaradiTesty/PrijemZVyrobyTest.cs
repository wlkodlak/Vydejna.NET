using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class PrijemZVyrobyTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.CenaNova = -2;
            Execute(cmd);
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 0;
            Execute(cmd);
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void NutneZadatPracoviste()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.KodPracoviste = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "KodPracoviste");
        }

        [TestMethod]
        public void NutneZadatStavNaradi()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.StavNaradi = StavNaradi.Neurcen;
            Execute(cmd);
            ChybaValidace("REQUIRED", "StavNaradi");
        }

        [TestMethod]
        public void PriNedostatkuNaradiChybaPoctu()
        {
            Vydane(4);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 8;
            Execute(cmd);
            ChybaStavu("RANGE", "Pocet");
        }

        [TestMethod]
        public void KopirujiSeDataZPrikazu()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(cmd.NaradiId, evnt.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, evnt.Pocet, "Pocet");
            Assert.AreEqual(cmd.KodPracoviste, evnt.KodPracoviste, "KodPracoviste");
            Assert.AreEqual(cmd.CenaNova, evnt.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.StavNaradi, evnt.StavNaradi, "StavNaradi");
        }

        [TestMethod]
        public void DoplniSeGenerovaneUdaje()
        {
            Vydane(10);
            Execute(ZakladniPrikaz());
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreNotEqual(Guid.Empty, evnt.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), evnt.Datum, "Datum");
        }

        [TestMethod]
        public void DoplniSePredchoziUmisteni()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.StavNaradi = StavNaradi.NutnoOpravit;
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(cmd.StavNaradi).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void DoplniSeNoveUmisteniPodleStavu()
        {
            Vydane(10);
            var cmd = ZakladniPrikaz();
            cmd.StavNaradi = StavNaradi.NutnoOpravit;
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(cmd.StavNaradi).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void SpocitaSeCelkovaPuvodniCena()
        {
            Vydane(3, 5m);
            Vydane(3, 7m);
            Vydane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(42m, evnt.CelkovaCenaPredchozi);
        }

        [TestMethod]
        public void CelkovaNovaCenaStejnaJakoPuvodniPriNeuvedeneCene()
        {
            Vydane(3, 5m);
            Vydane(3, 7m);
            Vydane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(evnt.CelkovaCenaPredchozi, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void CelkovaNovaCenaNasobkemPriZadaniCeny()
        {
            Vydane(3, 5m);
            Vydane(3, 7m);
            Vydane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            Assert.AreEqual(24m, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void NoveKusyMajiZadanouCenu()
        {
            Vydane(5, 4m, 1);
            Vydane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(3m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(3m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiPuvodniCenuPokudNebylaZadanaNova()
        {
            Vydane(5, 4m, 1);
            Vydane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(4m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(7m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiStejnaDataAPoctyJakoPuvodni()
        {
            Vydane(5, 4m, 1);
            Vydane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            var stare = evnt.PouziteKusy;
            var nove = evnt.NoveKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.IsNotNull(nove, "NoveKusy");
            Assert.AreEqual(stare.Count, nove.Count, "Count");
            for (int i = 0; i < stare.Count; i++)
            {
                Assert.AreEqual(stare[i].Pocet, nove[i].Pocet, "[{0}].Pocet");
                Assert.AreEqual(stare[i].Datum, nove[i].Datum, "[{0}].Datum");
            }
        }

        [TestMethod]
        public void VyberPouzitychKusu()
        {
            Vydane(5, 4m, 1);
            Vydane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZVyrobyEvent>();
            var stare = evnt.PouziteKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.AreEqual(2, stare.Count, "PouziteKusy.Count");
            Assert.AreEqual(Kus(Datum(1), 4m, 'P', 5), stare[0], "PouziteKusy[0]");
            Assert.AreEqual(Kus(Datum(2), 7m, 'P', 3), stare[1], "PouziteKusy[1]");
        }

        private NecislovaneNaradiPrijmoutZVyrobyCommand ZakladniPrikaz(int pocet = 8, string pracoviste = "84772140", decimal? cena = 10m, StavNaradi stav = StavNaradi.VPoradku)
        {
            return new NecislovaneNaradiPrijmoutZVyrobyCommand
            {
                NaradiId = _naradiId,
                Pocet = pocet,
                KodPracoviste = pracoviste,
                CenaNova = cena, 
                StavNaradi = stav
            };
        }

    }
}
