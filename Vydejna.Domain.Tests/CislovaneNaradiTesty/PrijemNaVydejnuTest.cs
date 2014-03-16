using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;
using Vydejna.Domain;
using Vydejna.Domain.ObecneNaradi;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class PrijemNaVydejnuTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CisloNaradiMusiBytKladne()
        {
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = new Guid("87228724-1111-1111-1111-222233334444"),
                CenaNova = 5,
                CisloNaradi = -1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            ChybaValidace("RANGE", "CisloNaradi");
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = new Guid("87228724-1111-1111-1111-222233334444"),
                CenaNova = -5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            ChybaValidace("RANGE", "CenaNova");
        }

        [TestMethod]
        public void CisloNaradiNesmiBytObsazeno()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            Given(naradiId,
                new CislovaneNaradiPrijatoNaVydejnuEvent
                {
                    NaradiId = naradiId,
                    CisloNaradi = 1,
                    EventId = Guid.NewGuid(),
                    Datum = new DateTime(2012, 1, 1),
                    KodDodavatele = "E7",
                    PrijemZeSkladu = false,
                    CenaNova = 3,
                    NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto()
                });
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CisloNaradi = 1,
                CenaNova = 5,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            ChybaStavu("CONFLICT", "CisloNaradi");
        }

        [TestMethod]
        public void GenerujeSePrijatoZeSkladu()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            Event<CislovaneNaradiPrijatoNaVydejnuEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void DoUdalostiSeGenerujiAutomatickeVlastnosti()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            });
            var udalost = NewEventOfType<CislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(0m, udalost.CenaPredchozi, "CenaPredchozi");
        }

        [TestMethod]
        public void DoUdalostiSeDoplnujiDataZPrijazu()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CenaNova, udalost.CenaNova, "CenaNova");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(cmd.KodDodavatele, udalost.KodDodavatele, "KodDodavatele");
            Assert.AreEqual(cmd.PrijemZeSkladu, udalost.PrijemZeSkladu, "PrijemZeSkladu");
        }

        [TestMethod]
        public void UmisteniNaradiNaVydejne()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = false
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPrijatoNaVydejnuEvent>();
            Assert.IsNotNull(udalost, "Ocekavana udalost prijmu");
            Assert.AreEqual(UmisteniNaradi.VeSkladu(), udalost.PredchoziUmisteni.ToValue(), "PredchoziUmisteni");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.VPoradku), udalost.NoveUmisteni.ToValue(), "NoveUmisteni");
        }

        [TestMethod]
        public void PriPrijmuZeSkladuSeSnizujeStavSkladuPomociInterniUdalosti()
        {
            var naradiId = new Guid("87228724-1111-1111-1111-222233334444");
            var cmd = new CislovaneNaradiPrijmoutNaVydejnuCommand
            {
                NaradiId = naradiId,
                CenaNova = 5,
                CisloNaradi = 1,
                KodDodavatele = "D58",
                PrijemZeSkladu = true
            };
            Execute(cmd);
            var udalost = NewEventOfType<NastalaPotrebaUpravitStavNaSkladeEvent>();
            Assert.IsNotNull(udalost, "Ocekavana udalost pro zmenu stavu na sklade");
            Assert.AreEqual(naradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(TypZmenyNaSklade.SnizitStav, udalost.TypZmeny, "TypZmeny");
            Assert.AreEqual(1, udalost.Hodnota, "Hodnota");
        }
    }
}
