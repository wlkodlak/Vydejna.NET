using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class VydejDoVyrobyTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Prijate(10);
            var cmd = ZakladniPrikaz();
            cmd.CenaNova = -2;
            Execute(cmd);
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Prijate(10);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 0;
            Execute(cmd);
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void NutneZadatPracoviste()
        {
            Prijate(10);
            var cmd = ZakladniPrikaz();
            cmd.KodPracoviste = "";
            Execute(cmd);
            ChybaValidace("REQUIRED", "KodPracoviste");
        }

        [TestMethod]
        public void PriNedostatkuNaradiChybaPoctu()
        {
            Prijate(4);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 8;
            Execute(cmd);
            ChybaStavu("RANGE", "Pocet");
        }

        [TestMethod]
        public void KopirujiSeDataZPrikazu()
        {
            Prijate(10);
            var cmd = new NecislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = _naradiId,
                Pocet = 8,
                KodPracoviste = "12341230",
                CenaNova = 10m
            };
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(cmd.NaradiId, evnt.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, evnt.Pocet, "Pocet");
            Assert.AreEqual(cmd.KodPracoviste, evnt.KodPracoviste, "KodPracoviste");
            Assert.AreEqual(cmd.CenaNova, evnt.CenaNova, "CenaNova");
        }

        [TestMethod]
        public void DoplniSeGenerovaneUdaje()
        {
            Prijate(10);
            Execute(ZakladniPrikaz());
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreNotEqual(Guid.Empty, evnt.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), evnt.Datum, "Datum");
            Assert.AreNotEqual(0, evnt.Verze, "Verze");
        }

        [TestMethod]
        public void DoplniSeUmisteni()
        {
            Prijate(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaPracovisti(cmd.KodPracoviste).Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void DoplniSePoctyNaUmisteni()
        {
            Vydane(5);
            Prijate(10);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(2, evnt.PocetNaPredchozim, "PocetNaPredchozim");
            Assert.AreEqual(13, evnt.PocetNaNovem, "PocetNaNovem");
        }

        [TestMethod]
        public void SpocitaSeCelkovaPuvodniCena()
        {
            Prijate(3, 5m);
            Prijate(3, 7m);
            Prijate(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(42m, evnt.CelkovaCenaPredchozi);
        }

        [TestMethod]
        public void CelkovaNovaCenaStejnaJakoPuvodniPriNeuvedeneCene()
        {
            Prijate(3, 5m);
            Prijate(3, 7m);
            Prijate(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(evnt.CelkovaCenaPredchozi, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void CelkovaNovaCenaNasobkemPriZadaniCeny()
        {
            Prijate(3, 5m);
            Prijate(3, 7m);
            Prijate(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            Assert.AreEqual(24m, evnt.CelkovaCenaNova);
        }

        [TestMethod]
        public void NoveKusyMajiCerstvostPouzite()
        {
            Prijate(5, 4m, 1);
            Prijate(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual("Pouzite", kusy[0].Cerstvost, "NoveKusy[0].Cerstvost");
            Assert.AreEqual("Pouzite", kusy[1].Cerstvost, "NoveKusy[1].Cerstvost");
        }

        [TestMethod]
        public void NoveKusyMajiZadanouCenu()
        {
            Prijate(5, 4m, 1);
            Prijate(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: 3m);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(3m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(3m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiPuvodniCenuPokudNebylaZadanaNova()
        {
            Prijate(5, 4m, 1);
            Prijate(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var kusy = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>().NoveKusy;
            Assert.IsNotNull(kusy, "NoveKusy");
            Assert.AreEqual(2, kusy.Count, "NoveKusy.Count");
            Assert.AreEqual(4m, kusy[0].Cena, "NoveKusy[0].Cena");
            Assert.AreEqual(7m, kusy[1].Cena, "NoveKusy[1].Cena");
        }

        [TestMethod]
        public void NoveKusyMajiStejnaDataAPoctyJakoPuvodni()
        {
            Prijate(5, 4m, 1);
            Prijate(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
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
            Prijate(5, 4m, 1);
            Prijate(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8, cena: null);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiVydanoDoVyrobyEvent>();
            var stare = evnt.PouziteKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.AreEqual(2, stare.Count, "PouziteKusy.Count");
            Assert.AreEqual(Kus(Datum(1), 4m, 'N', 5), stare[0], "PouziteKusy[0]");
            Assert.AreEqual(Kus(Datum(2), 7m, 'N', 3), stare[1], "PouziteKusy[1]");
        }

        private NecislovaneNaradiVydatDoVyrobyCommand ZakladniPrikaz(int pocet = 8, string pracoviste = "84772140", decimal? cena = 10m)
        {
            return new NecislovaneNaradiVydatDoVyrobyCommand
            {
                NaradiId = _naradiId,
                Pocet = pocet,
                KodPracoviste = pracoviste,
                CenaNova = cena
            };
        }
    }
}
