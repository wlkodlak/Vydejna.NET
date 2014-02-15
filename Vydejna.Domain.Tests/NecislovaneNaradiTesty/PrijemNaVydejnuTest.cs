using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class PrijemNaVydejnuTest : NecislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            var naradiId = Guid.NewGuid();
            Execute(new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            Execute(new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
        }

        [TestMethod]
        public void SpecifikacePouzitychKusu()
        {
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
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
            var naradiId = Guid.NewGuid();
            var cmd = new NecislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                Pocet = 7,
                CenaNova = 40m,
                KodDodavatele = "D58",
                PrijemZeSkladu = true
            };
            Execute(cmd);
            var udalost = NewEventOfType<NastalaPotrebaUpravitStavNaSkladeEvent>();
            Assert.AreEqual(naradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(TypZmenyNaSklade.SnizitStav, udalost.TypZmeny, "TypZmeny");
            Assert.AreEqual(7, udalost.Hodnota, "Hodnota");
        }
    }
}
