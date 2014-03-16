using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class VydejDoSrotuTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Znicene(10);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 0;
            Execute(cmd);
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void PriNedostatkuNaradiChybaPoctu()
        {
            Znicene(4);
            var cmd = ZakladniPrikaz();
            cmd.Pocet = 8;
            Execute(cmd);
            ChybaStavu("RANGE", "Pocet");
        }

        [TestMethod]
        public void KopirujiSeDataZPrikazu()
        {
            Znicene(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreEqual(cmd.NaradiId, evnt.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, evnt.Pocet, "Pocet");
        }

        [TestMethod]
        public void DoplniSeGenerovaneUdaje()
        {
            Znicene(10);
            Execute(ZakladniPrikaz());
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreNotEqual(Guid.Empty, evnt.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), evnt.Datum, "Datum");
            Assert.AreEqual(0m, evnt.CelkovaCenaNova, "CelkovaCenaNova");
        }

        [TestMethod]
        public void DoplniSeUmisteni()
        {
            Znicene(10);
            var cmd = ZakladniPrikaz();
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(), evnt.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.VeSrotu().Dto(), evnt.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void SpocitaSeCelkovaPuvodniCena()
        {
            Znicene(3, 5m);
            Neopravitelne(3, 7m);
            Znicene(2, 3m);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreEqual(42m, evnt.CelkovaCenaPredchozi);
        }

        [TestMethod]
        public void VyberPouzitychKusu()
        {
            Znicene(5, 4m, 1);
            Neopravitelne(5, 7m, 2);
            var cmd = ZakladniPrikaz(pocet: 8);
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            var stare = evnt.PouziteKusy;
            Assert.IsNotNull(stare, "PouziteKusy");
            Assert.AreEqual(2, stare.Count, "PouziteKusy.Count");
            Assert.AreEqual(Kus(Datum(1), 4m, 'P', 5), stare[0], "PouziteKusy[0]");
            Assert.AreEqual(Kus(Datum(2), 7m, 'P', 3), stare[1], "PouziteKusy[1]");
        }

        private NecislovaneNaradiPredatKeSesrotovaniCommand ZakladniPrikaz(int pocet = 8)
        {
            return new NecislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = _naradiId,
                Pocet = pocet
            };
        }
    }
}
