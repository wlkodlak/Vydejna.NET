using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class VydejDoSrotuTest : CislovaneNaradiServiceTestBase
    {
        [TestMethod]
        public void CislovaneNaradiMusiExistovat()
        {
            Execute(new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = Id("8394bb2a"),
                CisloNaradi = 4
            });
            ChybaStavu("NOTFOUND", "CisloNaradi");
        }

        [TestMethod]
        public void NoveNaradiNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4));
            Execute(new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4
            });
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void NeopravitelneNaradiZOpravyLzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtOpraveno(naradi, 4, opraveno: StavNaradiPoOprave.Neopravitelne));
            Execute(new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4
            });
            Event<CislovaneNaradiPredanoKeSesrotovaniEvent>();
            NoMoreEvents();
        }

        [TestMethod]
        public void NaradiVeSrotuNelzePouzit()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtSrotovano(naradi, 4));
            Execute(new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4
            });
            ChybaStavu("RANGE", "Umisteni");
        }

        [TestMethod]
        public void VUdalostiOdpovidajiHodnotyZPrikazu()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, EvtPrijato(naradi, 4), EvtVydano(naradi, 4), EvtVraceno(naradi, 4, stav: StavNaradi.Neopravitelne, vada: "9"));
            var cmd = new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreEqual(cmd.NaradiId, udalost.NaradiId, "NaradiId");
            Assert.AreEqual(cmd.CisloNaradi, udalost.CisloNaradi, "CisloNaradi");
            Assert.AreEqual(UmisteniNaradi.VeSrotu().Dto(), udalost.NoveUmisteni, "NoveUmisteni");
        }

        [TestMethod]
        public void VUdalostiSeGenerujiAutomatickeHodnoty()
        {
            var naradi = Id("8394bb2a");
            Given(naradi, 
                EvtPrijato(naradi, 4), 
                EvtVydano(naradi, 4), 
                EvtVraceno(naradi, 4, stav: StavNaradi.Neopravitelne, vada: "9", cenaPo: 300m));
            var cmd = new CislovaneNaradiPredatKeSesrotovaniCommand
            {
                NaradiId = naradi,
                CisloNaradi = 4
            };
            Execute(cmd);
            var udalost = NewEventOfType<CislovaneNaradiPredanoKeSesrotovaniEvent>();
            Assert.AreNotEqual(Guid.Empty, udalost.EventId, "EventId");
            Assert.AreEqual(GetUtcTime(), udalost.Datum, "Datum");
            Assert.AreEqual(300m, udalost.CenaPredchozi, "CenaPredchozi");
            Assert.AreEqual(UmisteniNaradi.NaVydejne(StavNaradi.Neopravitelne).Dto(), udalost.PredchoziUmisteni, "PredchoziUmisteni");
        }
    }
}
