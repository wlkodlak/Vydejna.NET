using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class PrijemNaVydejnuTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Execute(new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = -3m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void PocetMusiBytKladny()
        {
            Execute(new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 0,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            ChybaValidace("RANGE", "Pocet");
        }

        [TestMethod]
        public void ZPrikazuSeKopirujiPole()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.Pocet, udalost.Pocet, "Pocet");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.KodDodavatele, udalost.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.PrijemZeSkladu, udalost.PrijemZeSkladu, "PrijemZeSkladu");
        }

        [TestMethod]
        public void CelkovaCena()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.AreEqual(0m, udalost.CelkovaCenaPredchozi, "CelkovaCenaPredchozi");
            Assert.AreEqual(280m, udalost.CelkovaCenaNova, "CelkovaCenaNova");
        }

        [TestMethod]
        public void ZakladniGenerovaneUdaje()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(UmisteniNaradi.VeSkladu().Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(), udalost.NoveUmisteni, "NoveUmisteni");
            Assert.AreEqual(1, udalost.Verze, "Verze");
        }

        [TestMethod]
        public void PocetKusuNaUmisteni()
        {
            Prijate(3);
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.AreEqual(0, udalost.PocetNaPredchozim, "PocetNaPredchozim");
            Assert.AreEqual(10, udalost.PocetNaNovem, "PocetNaNovem");
        }

        [TestMethod]
        public void SpecifikacePouzitychKusu()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var evnt = NewEventOfType<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.IsNull(evnt.PouziteKusy, "PouziteKusy");
            OcekavaneKusy<NecislovaneNaradiPrijatoNaVydejnuEvent>(e => e.NoveKusy,
                Kus(GetUtcTime(), 40m, 'N', 7));
        }


        [TestMethod]
        public void PriPrijmuBezSkladuJenZakladniUdalost()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            Event<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void PriPrijmuZeSkladuNavicUdalost()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = true
            };
            Execute(cmd);
            Event<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            Event<NastalaPotrebaUpravitStavNaSkladeEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void UdalostZmenyNaSklade()
        {
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = _naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = true
            };
            Execute(cmd);
            var udalost = NewEventOfType<NastalaPotrebaUpravitStavNaSkladeEvent>();
            Assert.AreEqual(_naradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(TypZmenyNaSklade.SnizitStav, udalost.TypZmeny, "TypZmeny");
            Assert.AreEqual(7, udalost.Hodnota, "Hodnota");
            Assert.AreEqual(2, udalost.Verze, "Verze");
        }
    }
}
