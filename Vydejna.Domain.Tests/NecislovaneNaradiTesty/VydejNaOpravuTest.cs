using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class VydejNaOpravuTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            cmd.CenaNova = -2;
            Execute(cmd);
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 0;
            Execute(cmd);
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void NutneZadatDodavatele()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            cmd.KodDodavatele = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "KodDodavatele");
        }
        [TestMethod]
        public void NutneZadatObjednavku()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            cmd.Objednavka = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "Objednavka");
        }

        [TestMethod]
        public void NutneZadatTypOpravy()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            cmd.TypOpravy = TypOpravy.Zadna;
            Execute(cmd);
            ChybaValidace("REQUIRED", "TypOpravy");
        }

        [TestMethod]
        public void PriNedostatkuNaradiChybaPoctu()
        {
            Poskozene(4);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 8;
            Execute(cmd);
            ChybaStavu("RANGE", "Pocet");
        }

        [TestMethod]
        public void KopirujiSeDataZPrikazu()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(cmd.NaradiId, evnt.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, evnt.Pocet, "Pocet");
            Assert.AreEqual(cmd.CenaNova, evnt.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.KodDodavatele, evnt.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.Objednavka, evnt.Objednavka, "Objednavka");
            Assert.AreEqual(cmd.TerminDodani, evnt.TerminDodani, "TerminDodani");
            Assert.AreEqual(cmd.TypOpravy, evnt.TypOpravy, "TypOpravy");
        }

        [TestMethod]
        public void DoplniSeGenerovaneUdaje()
        {
            Poskozene(10);
            Execute(ZakladniPrikaz());
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreNotEqual(Guid.Empty, evnt.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), evnt.Datum, "Datum");
        }

        [TestMethod]
        public void DoplniSeUmisteni()
        {
            Poskozene(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.NutnoOpravit).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaOprave(cmd.TypOpravy, cmd.KodDodavatele, cmd.Objednavka).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void SpocitaSeCelkovaPuvodniCena()
        {
            Poskozene(3, 5m);
            Poskozene(3, 7m);
            Poskozene(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(42m, evnt.CelkovaCenaPredchozi);
        }

        [TestMethod]
        public void CelkovaNovaCenaStejnaJakoPuvodniPriNeuvedeneCene()
        {
            Poskozene(3, 5m);
            Poskozene(3, 7m);
            Poskozene(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(evnt.CelkovaCenaPredchozi, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void CelkovaNovaCenaNasobkemPriZadaniCeny()
        {
            Poskozene(3, 5m);
            Poskozene(3, 7m);
            Poskozene(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            Assert.AreEqual(24m, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void NoveKusyMajiCerstvostPouzite()
        {
            Poskozene(5, 4m, 1);
            Poskozene(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual("Pouzite", kusy[0].Cerstvost, "NoveKusy[0].Cerstvost");
            Assert.AreEqual("Pouzite", kusy[1].Cerstvost, "NoveKusy[1].Cerstvost");
        }

        [TestMethod]
        public void NoveKusyMajiZadanouCenu()
        {
            Poskozene(5, 4m, 1);
            Poskozene(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(3m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(3m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiPuvodniCenuPokudNebylaZadanaNova()
        {
            Poskozene(5, 4m, 1);
            Poskozene(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(4m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(7m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiStejnaDataAPoctyJakoPuvodni()
        {
            Poskozene(5, 4m, 1);
            Poskozene(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
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
            Poskozene(5, 4m, 1);
            Poskozene(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKOpraveEvent>();
            var stare = evnt.PouziteKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.AreEqual(2, stare.Count, "PouziteKusy.Count");
            Assert.AreEqual(Kus(Datum(1), 4m, 'P', 5), stare[0], "PouziteKusy[0]");
            Assert.AreEqual(Kus(Datum(2), 7m, 'P', 3), stare[1], "PouziteKusy[1]");
        }

        private NecislovaneNaradiPredatKOpraveCommand ZakladniPrikaz(int pocet = 8, string objednavka = "111/2014", decimal? cena = 10m, string dodavatel = "D48", TypOpravy typOpravy = TypOpravy.Oprava)
        {
            return new NecislovaneNaradiPredatKOpraveCommand
            {
                NaradiId = _naradiId,
                Pocet = pocet,
                CenaNova = cena,
                KodDodavatele = dodavatel,
                Objednavka = objednavka,
                TerminDodani = GetUtcTime().Date.AddDays(30),
                TypOpravy = typOpravy
            };
        }
    }
}
