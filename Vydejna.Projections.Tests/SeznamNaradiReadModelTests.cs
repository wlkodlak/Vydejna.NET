using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Projections.SeznamNaradiReadModel;

namespace Vydejna.Projections.Tests
{
    [TestClass]
    public class SeznamNaradiReadModelTests : ReadModelTestBase
    {
        private ZiskatSeznamNaradiResponse _response;
        private int _sequenceGuid;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _response = null;
            _sequenceGuid = 0;
        }

        [TestMethod]
        public void NeexistujiciDataDavajiPrazdnyVysledek()
        {
            ZiskatStranku();
            Assert.AreEqual(0, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(0, _response.PocetStranek, "PocetStranek");
            Assert.AreEqual(1, _response.Stranka, "Stranka");
            Assert.IsNotNull(_response.SeznamNaradi, "SeznamNaradi");
            Assert.AreEqual(0, _response.SeznamNaradi.Count, "SeznamNaradi.Count");
        }

        [TestMethod]
        public void DefinovaneNaradiMaVlastnostiPodleUdalosti()
        {
            var definiceNaradi = new DefinovanoNaradiEvent
            {
                NaradiId = GetGuid(),
                Verze = 1,
                Vykres = "888-809",
                Rozmer = "prum.36 č.5",
                Druh = "opěrka"
            };
            SendEvent(definiceNaradi);
            ZiskatStranku();
            Assert.AreNotEqual(0, _response.SeznamNaradi.Count, "SeznamNaradi.Count");
            var naradi = _response.SeznamNaradi[0];
            Assert.AreEqual(definiceNaradi.NaradiId, naradi.Id, "Id");
            Assert.AreEqual(definiceNaradi.Vykres, naradi.Vykres, "Vykres");
            Assert.AreEqual(definiceNaradi.Rozmer, naradi.Rozmer, "Rozmer");
            Assert.AreEqual(definiceNaradi.Druh, naradi.Druh, "Druh");
            Assert.AreEqual(true, naradi.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void DefiniceNaradiPridavaNoveRadky()
        {
            SendDefinovanoNaradi("333-111", "20x30", "bruska");
            SendDefinovanoNaradi("842-384", "prum.10", "operka");
            SendDefinovanoNaradi("888-809", "prum.36", "operka");
            ZiskatStranku();
            Assert.AreEqual(3, _response.PocetCelkem, "PocetCelkem");
            var seznamVykresu = string.Join(", ", _response.SeznamNaradi.Select(n => n.Vykres));
            var ocekavane = "333-111, 842-384, 888-809";
            Assert.AreEqual(ocekavane, seznamVykresu, "SeznamNaradi[].Vykres");
        }

        [TestMethod]
        public void DeaktivaceNaradiNemazeNaradiZeSeznamu()
        {
            SendDefinovanoNaradi("333-111", "20x30", "bruska");
            var naradiId = SendDefinovanoNaradi("842-384", "prum.10", "operka");
            SendDefinovanoNaradi("888-809", "prum.36", "operka");
            SendDeaktivovanoNaradi(naradiId);
            ZiskatStranku();
            Assert.AreEqual(3, _response.PocetCelkem, "PocetCelkem");
            var seznamVykresu = string.Join(", ", _response.SeznamNaradi.Select(n => n.Vykres));
            var ocekavane = "333-111, 842-384, 888-809";
            Assert.AreEqual(ocekavane, seznamVykresu, "SeznamNaradi[].Vykres");
        }

        [TestMethod]
        public void DeaktivaceNaradiUpravujeStavNaradi()
        {
            SendDefinovanoNaradi("333-111", "20x30", "bruska");
            var naradiId = SendDefinovanoNaradi("842-384", "prum.10", "operka");
            SendDefinovanoNaradi("888-809", "prum.36", "operka");
            SendDeaktivovanoNaradi(naradiId);
            var naradi = ZiskatNaradi(naradiId);
            Assert.AreEqual(false, naradi.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void AktivaceNaradiUpravujeStavNaradi()
        {
            SendDefinovanoNaradi("333-111", "20x30", "bruska");
            var naradiId = SendDefinovanoNaradi("842-384", "prum.10", "operka");
            SendDefinovanoNaradi("888-809", "prum.36", "operka");
            SendDeaktivovanoNaradi(naradiId);
            SendAktivovanoNaradi(naradiId);
            var naradi = ZiskatNaradi(naradiId);
            Assert.AreEqual(true, naradi.Aktivni, "Aktivni");
        }

        [TestMethod]
        public void SeznamJeSerazenPodleVykresuARozmeru()
        {
            SendDefinovanoNaradi("842-384", "prum.12", "operka");
            SendDefinovanoNaradi("333-111", "20x30", "bruska");
            SendDefinovanoNaradi("842-384", "prum.10", "operka");
            SendDefinovanoNaradi("888-809", "prum.36", "operka");
            ZiskatStranku();
            for (int i = 1; i < _response.SeznamNaradi.Count; i++)
            {
                var a = _response.SeznamNaradi[i - 1];
                var b = _response.SeznamNaradi[i];
                var vykresCompare = string.Compare(a.Vykres, b.Vykres);
                Assert.AreNotEqual(1, vykresCompare, "Vykres: {0} <= {1}", a.Vykres, b.Vykres);
                if (vykresCompare == 0)
                    Assert.AreEqual(-1, string.Compare(a.Rozmer, b.Rozmer), "Rozmer: {0} < {1}", a.Rozmer, b.Rozmer);
            }
        }

        [TestMethod]
        public void MetadataPriVelkemMnozstviNaradi()
        {
            GenerovatSpoustuNaradi(385);
            ZiskatStranku(2);
            Assert.AreEqual(2, _response.Stranka, "Stranka");
            Assert.AreEqual(385, _response.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(4, _response.PocetStranek, "PocetStranek");
            Assert.AreEqual(100, _response.SeznamNaradi.Count, "SeznamNaradi.Count");
        }

        [TestMethod]
        public void ObsahDruheStrankySeznamu()
        {
            GenerovatSpoustuNaradi(385);
            ZiskatStranku(2);
            var prvniVykres = VykresVGenerovanemSeznamu(100);
            var posledniVykres = VykresVGenerovanemSeznamu(199);
            var prvniRozmer = RozmerVGenerovanemSeznamu(100);
            var posledniRozmer = RozmerVGenerovanemSeznamu(199);
            Assert.AreEqual(prvniVykres, _response.SeznamNaradi[0].Vykres, "SeznamNaradi[0 + 100].Vykres");
            Assert.AreEqual(posledniVykres, _response.SeznamNaradi[99].Vykres, "SeznamNaradi[99 + 100].Vykres");
            Assert.AreEqual(prvniRozmer, _response.SeznamNaradi[0].Rozmer, "SeznamNaradi[0 + 100].Rozmer");
            Assert.AreEqual(posledniRozmer, _response.SeznamNaradi[99].Rozmer, "SeznamNaradi[99 + 100].Rozmer");
        }

        private void GenerovatSpoustuNaradi(int pocet)
        {
            for (int r = 0; r < 4; r++)
            {
                for (int i = 0; i < pocet; i++)
                {
                    if ((i & 3) != r)
                        continue;
                    SendDefinovanoNaradi(i);
                }
            }
        }

        private Guid GetGuid()
        {
            _sequenceGuid++;
            return GetGuid(_sequenceGuid);
        }

        private static Guid GetGuid(int sequence)
        {
            return new Guid(sequence, 0, 0, new byte[8]);
        }

        private void ZiskatStranku(int stranka = 1)
        {
            _response = ReadProjection<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(new ZiskatSeznamNaradiRequest(stranka));
        }

        private TypNaradiDto ZiskatNaradi(Guid naradiId)
        {
            ZiskatStranku();
            foreach (var naradi in _response.SeznamNaradi)
            {
                if (naradi.Id == naradiId)
                    return naradi;
            }
            Assert.Fail("Nenalezeno naradi {0}", naradiId);
            return null;
        }

        private Guid SendDefinovanoNaradi(string vykres, string rozmer, string druh)
        {
            var naradiId = GetGuid();
            SendEvent(new DefinovanoNaradiEvent
            {
                NaradiId = naradiId,
                Verze = 1,
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh
            });
            return naradiId;
        }

        private void SendDefinovanoNaradi(int order)
        {
            var vykres = VykresVGenerovanemSeznamu(order);
            var rozmer = RozmerVGenerovanemSeznamu(order);
            SendDefinovanoNaradi(vykres, rozmer, "");
        }

        private static string VykresVGenerovanemSeznamu(int order)
        {
            return string.Format("839-{0:00000}", order >> 4);
        }

        private static string RozmerVGenerovanemSeznamu(int order)
        {
            return string.Format("{0:000}x{0:000}", order & 15);
        }

        private void SendDeaktivovanoNaradi(Guid naradiId, int verze = 2)
        {
            SendEvent(new DeaktivovanoNaradiEvent
            {
                NaradiId = naradiId,
                Verze = verze
            });
        }

        private void SendAktivovanoNaradi(Guid naradiId, int verze = 3)
        {
            SendEvent(new AktivovanoNaradiEvent
            {
                NaradiId = naradiId,
                Verze = verze
            });
        }

        protected override IEventProjection CreateProjection()
        {
            return new SeznamNaradiProjection(new SeznamNaradiRepository(_folder), _executor, _time);
        }

        protected override object CreateReader()
        {
            return new SeznamNaradiReader(new SeznamNaradiRepository(_folder), _executor, _time);
        }
    }
}
