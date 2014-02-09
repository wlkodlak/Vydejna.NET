using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    [TestClass]
    public class MnozinaNecislovanehoNaradiTest
    {
        [TestMethod]
        public void PrazdnaMnozina()
        {
            Ocekavana();
            Soucet(0);
        }

        [TestMethod]
        public void PridaniJedneSkupiny()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            Ocekavana(Skupina(4, 10, 'N', 5));
            Soucet(5);
        }

        [TestMethod]
        public void PridaniJineSkupiny()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));
            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
            Soucet(15);
        }

        [TestMethod]
        public void PridaniShodneSkupinyPricitaPocet()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            _mnozina.Pridat(Skupina(7, 30, 'P', 6));

            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(7, 30, 'P', 9),
                Skupina(9, 20, 'O', 7));
            Soucet(21);
        }

        [TestMethod]
        public void PridavaniRuznychDatumuZachovavaRazeni()
        {
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
            Soucet(16);
        }

        [TestMethod]
        public void PridavaniStejnychDatumuZachovavaRazeni()
        {
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));
            _mnozina.Pridat(Skupina(7, 15, 'P', 1));
            Poradi(
                Skupina(4, 10, 'N', 5),
                Skupina(7, 15, 'P', 1),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
            Soucet(16);
        }

        [TestMethod]
        public void OdebiraniSnizujePocet()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            _mnozina.Odebrat(Skupina(7, 30, 'P', 1));

            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 2),
                Skupina(9, 20, 'O', 7));
            Soucet(15);
        }

        [TestMethod]
        public void OdebiraniOdstranujeNuloveSkupiny()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            _mnozina.Odebrat(Skupina(5, 15, 'P', 1));

            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
            Soucet(15);
        }

        [TestMethod]
        public void OdebiraniNeexistujiciSkupinyNemeniSeznam()
        {
            // toto chovani je tu kvuli odolnosti vuci chybam v udalostech
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            _mnozina.Odebrat(Skupina(5, 20, 'P', 2));

            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
        }

        [TestMethod]
        public void CelkovyPocetJeNezavislyNaSouctuSkupin()
        {
            // toto chovani je tu kvuli odolnosti vuci chybam v udalostech
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Odebrat(Skupina(5, 20, 'P', 2));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            Soucet(14);
        }

        [TestMethod]
        public void PouzivaniVraciNekolikPrvnichSkupin()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            Pouzit(6,
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1));
        }

        [TestMethod]
        public void PouzivaniVraciVyzadanyPocet()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            Pouzit(8,
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 2));
        }

        [TestMethod]
        public void PouzivaniNemeniObsah()
        {
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));

            _mnozina.Pouzit(8);

            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 7));
        }

        [TestMethod]
        public void SnizeniCelkovehoPoctuNaNuluCistiSeznamSkupin()
        {
            // toto chovani je tu kvuli odolnosti vuci chybam v udalostech
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Odebrat(Skupina(3, 12, 'P', 4));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Odebrat(Skupina(4, 10, 'N', 1));
            _mnozina.Odebrat(Skupina(5, 15, 'P', 1));

            Soucet(0);
            Ocekavana();
        }

        [TestMethod]
        public void UmoznenaKorekceSouctu()
        {
            // toto chovani je tu kvuli odolnosti vuci chybam v udalostech ve spojeni se snapshoty
            _mnozina.Pridat(Skupina(4, 10, 'N', 5));
            _mnozina.Pridat(Skupina(5, 15, 'P', 1));
            _mnozina.Pridat(Skupina(7, 30, 'P', 3));
            _mnozina.PocetCelkem = 7;
            _mnozina.Pridat(Skupina(9, 20, 'O', 7));
            _mnozina.Odebrat(Skupina(9, 20, 'O', 2));
            Ocekavana(
                Skupina(4, 10, 'N', 5),
                Skupina(5, 15, 'P', 1),
                Skupina(7, 30, 'P', 3),
                Skupina(9, 20, 'O', 5));
            Soucet(12);
        }

        private MnozinaNecislovanehoNaradi _mnozina;

        [TestInitialize]
        public void Initialize()
        {
            _mnozina = new MnozinaNecislovanehoNaradi();
        }

        private static SkupinaNecislovanehoNaradi Skupina(int datum, int cena, char cerstvost, int pocet)
        {
            return new SkupinaNecislovanehoNaradi(
                new DateTime(2014, 1, 1).AddDays(datum),
                cena,
                Cerstvost(cerstvost),
                pocet
            );
        }

        private static CerstvostNecislovanehoNaradi Cerstvost(char cerstvost)
        {
            switch (cerstvost)
            {
                case 'N': return CerstvostNecislovanehoNaradi.Nove;
                case 'O': return CerstvostNecislovanehoNaradi.Opravene;
                default: return CerstvostNecislovanehoNaradi.Pouzite;
            }
        }

        private static char Cerstvost(CerstvostNecislovanehoNaradi cerstvost)
        {
            switch (cerstvost)
            {
                case CerstvostNecislovanehoNaradi.Nove: return 'N';
                case CerstvostNecislovanehoNaradi.Opravene: return 'O';
                default: return 'P';
            }
        }

        private void PorovnatObsahy(IEnumerable<SkupinaNecislovanehoNaradi> ocekavano, IEnumerable<SkupinaNecislovanehoNaradi> skutecne, string zprava)
        {
            var strRealne = new StringBuilder().AppendLine();
            var strSpravne = new StringBuilder().AppendLine();
            foreach (var skupina in skutecne)
            {
                strRealne.AppendFormat("Datum:{0},Cena:{1},Cerstvost:{2},Pocet:{3}",
                    CisloData(skupina), (int)skupina.Cena, Cerstvost(skupina.Cerstvost), skupina.Pocet).AppendLine();
            }
            foreach (var skupina in ocekavano)
            {
                strSpravne.AppendFormat("Datum:{0},Cena:{1},Cerstvost:{2},Pocet:{3}",
                    CisloData(skupina), (int)skupina.Cena, Cerstvost(skupina.Cerstvost), skupina.Pocet).AppendLine();
            }
            Assert.AreEqual(strSpravne.ToString(), strRealne.ToString(), zprava);
        }

        private static int CisloData(SkupinaNecislovanehoNaradi skupina)
        {
            return (int)(skupina.DatumCerstvosti - new DateTime(2014, 1, 1)).TotalDays;
        }

        private void Ocekavana(params SkupinaNecislovanehoNaradi[] skupiny)
        {
            var obsah = _mnozina.Obsah();
            PorovnatObsahy(skupiny, obsah, "Obsah");
        }

        private void Poradi(params SkupinaNecislovanehoNaradi[] skupiny)
        {
            var obsah = _mnozina.Obsah();

            var datumyObsah = string.Join(", ", obsah.Select(ElementPoradi));
            var datumyOcekavane = string.Join(", ", skupiny.Select(ElementPoradi));
            Assert.AreEqual(datumyOcekavane, datumyObsah, "Zakladni poradi");
        }

        private static string ElementPoradi(SkupinaNecislovanehoNaradi s)
        {
            return CisloData(s).ToString();
        }

        private void Soucet(int soucet)
        {
            Assert.AreEqual(soucet, _mnozina.PocetCelkem, "PocetCelkem");
        }

        private void Pouzit(int pocet, params SkupinaNecislovanehoNaradi[] skupiny)
        {
            var pouzite = _mnozina.Pouzit(pocet);
            PorovnatObsahy(skupiny, pouzite, "Pouzite");
        }
    }
}
