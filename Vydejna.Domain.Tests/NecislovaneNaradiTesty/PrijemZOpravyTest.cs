using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class PrijemZOpravyTest : NecislovaneNaradiServiceTestBase
    {
        /*
         * Cena nesmi byt zaporna
         * Pocet musi byt kladny
         * Nutne zadat dodavatele
         * Nutne zadat objednavku
         * Nutne zadat dodaci list
         * Nutne urcit typ opravy
         * Nutne urcit vysledek opravy
         */
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.CenaNova = -2;
            Execute(cmd);
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 0;
            Execute(cmd);
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void NutneZadatDodavatele()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.KodDodavatele = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "KodDodavatele");
        }

        [TestMethod]
        public void NutneZadatObjednavku()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.Objednavka = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "Objednavka");
        }

        [TestMethod]
        public void NutneZadatDodaciList()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.DodaciList = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "DodaciList");
        }

        [TestMethod]
        public void NutneZadatTypOpravy()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.TypOpravy = TypOpravy.Zadna;
            Execute(cmd);
            ChybaValidace("REQUIRED", "TypOpravy");
        }

        [TestMethod]
        public void NutneZadatVysledekOpravy()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.Opraveno = StavNaradiPoOprave.Neurcen;
            Execute(cmd);
            ChybaValidace("REQUIRED", "Opraveno");
        }

        [TestMethod]
        public void PriNedostatkuNaradiChybaPoctu()
        {
            Opravovane(4);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 8;
            Execute(cmd);
            ChybaStavu("RANGE", "Pocet");
        }

        [TestMethod]
        public void KopirujiSeDataZPrikazu()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(cmd.NaradiId, evnt.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, evnt.Pocet, "Pocet");
            Assert.AreEqual(cmd.CenaNova, evnt.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.KodDodavatele, evnt.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.Objednavka, evnt.Objednavka, "Objednavka");
            Assert.AreEqual(cmd.DodaciList, evnt.DodaciList, "DodaciList");
            Assert.AreEqual(cmd.Opraveno, evnt.Opraveno, "Opraveno");
            Assert.AreEqual(cmd.TypOpravy, evnt.TypOpravy, "TypOpravy");
        }

        [TestMethod]
        public void DoplniSeGenerovaneUdaje()
        {
            Opravovane(10);
            Execute(ZakladniPrikaz());
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreNotEqual(Guid.Empty, evnt.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), evnt.Datum, "Datum");
        }

        [TestMethod]
        public void DoplniSePuvodniUmisteni()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
        }

        [TestMethod]
        public void DoplniSeNoveUmisteni_Opraveno()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.Opraveno = StavNaradiPoOprave.Opraveno;
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void DoplniSeNoveUmisteni_Neopravitelne()
        {
            Opravovane(10);
            var cmd = ZakladniPrikaz();
            cmd.Opraveno = StavNaradiPoOprave.Neopravitelne;
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void SpocitaSeCelkovaPuvodniCena()
        {
            Opravovane(3, 5m);
            Opravovane(3, 7m);
            Opravovane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(42m, evnt.CelkovaCenaPredchozi);
        }

        [TestMethod]
        public void CelkovaNovaCenaStejnaJakoPuvodniPriNeuvedeneCene()
        {
            Opravovane(3, 5m);
            Opravovane(3, 7m);
            Opravovane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(evnt.CelkovaCenaPredchozi, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void CelkovaNovaCenaNasobkemPriZadaniCeny()
        {
            Opravovane(3, 5m);
            Opravovane(3, 7m);
            Opravovane(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            Assert.AreEqual(24m, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void NoveKusyMajiCerstvostOpravene()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            cmd.Opraveno = StavNaradiPoOprave.Opraveno;
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual("Opravene", kusy[0].Cerstvost, "NoveKusy[0].Cerstvost");
            Assert.AreEqual("Opravene", kusy[1].Cerstvost, "NoveKusy[1].Cerstvost");
        }

        [TestMethod]
        public void NoveKusyMajiCerstvostPouzitePriNeopravitelnosti()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            cmd.Opraveno = StavNaradiPoOprave.Neopravitelne;
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual("Pouzite", kusy[0].Cerstvost, "NoveKusy[0].Cerstvost");
            Assert.AreEqual("Pouzite", kusy[1].Cerstvost, "NoveKusy[1].Cerstvost");
        }

        [TestMethod]
        public void NoveKusyMajiCerstvostPouzitePokudOpravaNebylaPotreba()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            cmd.Opraveno = StavNaradiPoOprave.OpravaNepotrebna;
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual("Pouzite", kusy[0].Cerstvost, "NoveKusy[0].Cerstvost");
            Assert.AreEqual("Pouzite", kusy[1].Cerstvost, "NoveKusy[1].Cerstvost");
        }

        [TestMethod]
        public void NoveKusyMajiZadanouCenu()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(3m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(3m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiPuvodniCenuPokudNebylaZadanaNova()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(4m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(7m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiStejnaDataAPoctyJakoPuvodni()
        {
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
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
            Opravovane(5, 4m, 1);
            Opravovane(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoZOpravyEvent>();
            var stare = evnt.PouziteKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.AreEqual(2, stare.Count, "PouziteKusy.Count");
            Assert.AreEqual(Kus(Datum(1), 4m, 'P', 5), stare[0], "PouziteKusy[0]");
            Assert.AreEqual(Kus(Datum(2), 7m, 'P', 3), stare[1], "PouziteKusy[1]");
        }

        private NecislovaneNaradiPrijmoutZOpravyCommand ZakladniPrikaz(
            int pocet = 8, string objednavka = "111/2014", decimal? cena = 10m, 
            string dodavatel = "D48", TypOpravy typOpravy = TypOpravy.Oprava,
            string dodaciList = "111d/2014", StavNaradiPoOprave opraveno = StavNaradiPoOprave.Opraveno)
        {
            return new NecislovaneNaradiPrijmoutZOpravyCommand
            {
                NaradiId = _naradiId,
                Pocet = pocet,
                CenaNova = cena,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                DodaciList = dodaciList,
                Opraveno = opraveno,
                TypOpravy = typOpravy
            };
        }
    }
}
