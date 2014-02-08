using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class SkupinaNecislovanehoNaradiTest
    {
        [TestMethod]
        public void KonstrukceZeSoucasti()
        {
            var datum = new DateTime(2013, 8, 13, 22, 43, 33);
            var skupina = new SkupinaNecislovanehoNaradi(datum, 30m, CerstvostNecislovanehoNaradi.Nove, 12);
            Assert.AreEqual(datum, skupina.DatumCerstvosti, "DatumCerstvosti");
            Assert.AreEqual(30m, skupina.Cena, "Cena");
            Assert.AreEqual(CerstvostNecislovanehoNaradi.Nove, skupina.Cerstvost, "Cerstvost");
            Assert.AreEqual(12, skupina.Pocet, "Pocet");
        }

        [TestMethod]
        public void Equality()
        {
            var a = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var b = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var c = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 8);
            var d = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var e = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var f = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 5m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var g = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 10), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            Assert.AreEqual(a, b);
            Assert.AreEqual(d, e);
            Assert.AreNotEqual(b, c);
            Assert.AreNotEqual(a, d);
            Assert.AreNotEqual(d, f);
            Assert.AreNotEqual(d, g);
        }

        [TestMethod]
        public void PricitaniZvysujePocet()
        {
            var original = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var expected = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 18);
            var actual = original.Pridat(6);
            Assert.AreEqual(expected, actual, "Po pricteni");
        }

        [TestMethod]
        public void PricitaniNemeniPuvodniInstanci()
        {
            var original = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var before = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            before.Pridat(6);
            Assert.AreEqual(before, original, "Zakazana modifikace");
        }

        [TestMethod]
        public void OdecitaniSnizujePocet()
        {
            var original = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var expected = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 8);
            var actual = original.Odebrat(4);
            Assert.AreEqual(expected, actual, "Po odecteni");
        }

        [TestMethod]
        public void OdecitaniNejdePodNulu()
        {
            var original = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 3);
            var expected = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 0);
            var actual = original.Odebrat(4);
            Assert.AreEqual(expected, actual, "Po odecteni");
        }

        [TestMethod]
        public void OdecitaniNemeniObjekt()
        {
            var original = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var before = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            before.Odebrat(4);
            Assert.AreEqual(before, original, "Zakazana modifikace");
        }

        [TestMethod]
        public void ShodaNezohlednujePocet()
        {
            var a = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var b = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 12);
            var c = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Nove, 8);
            var d = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var e = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var f = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 12), 5m, CerstvostNecislovanehoNaradi.Opravene, 12);
            var g = new SkupinaNecislovanehoNaradi(new DateTime(2013, 4, 10), 10m, CerstvostNecislovanehoNaradi.Opravene, 12);
            Assert.IsTrue(a.Odpovida(b), "{0} ~ {1}");
            Assert.IsTrue(a.Odpovida(c), "{0} ~ {1}");
            Assert.IsTrue(!a.Odpovida(d), "{0} !~ {1}");
            Assert.IsTrue(d.Odpovida(e), "{0} ~ {1}");
            Assert.IsTrue(!d.Odpovida(f), "{0} !~ {1}");
            Assert.IsTrue(!d.Odpovida(g), "{0} !~ {1}");
        }
    }
}
