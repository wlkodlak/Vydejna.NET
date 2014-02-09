using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public class MnozinaNecislovanehoNaradi
    {
        private List<SkupinaNecislovanehoNaradi> _data;
        private int _pocetCelkem;
        private IComparer<SkupinaNecislovanehoNaradi> _comparer;

        public MnozinaNecislovanehoNaradi()
        {
            _data = new List<SkupinaNecislovanehoNaradi>();
            _pocetCelkem = 0;
            _comparer = new SkupinaNecislovanehoNaradiComparer();
        }

        public int PocetCelkem
        {
            get { return _pocetCelkem; }
            set { _pocetCelkem = value; }
        }

        public IList<SkupinaNecislovanehoNaradi> Obsah()
        {
            return _data;
        }

        public void Pridat(SkupinaNecislovanehoNaradi skupina)
        {
            var pozice = NajitSkupinu(skupina);
            if (pozice >= 0)
                _data[pozice] = _data[pozice].Pridat(skupina.Pocet);
            else
                _data.Insert(~pozice, skupina);
            _pocetCelkem += skupina.Pocet;
        }

        public void Odebrat(SkupinaNecislovanehoNaradi skupina)
        {
            _pocetCelkem -= skupina.Pocet;
            if (_pocetCelkem <= 0)
            {
                _pocetCelkem = 0;
                _data.Clear();
            }
            else
            {
                var pozice = NajitSkupinu(skupina);
                if (pozice >= 0)
                {
                    var novaHodnota = _data[pozice].Odebrat(skupina.Pocet);
                    if (novaHodnota.Pocet > 0)
                        _data[pozice] = novaHodnota;
                    else
                        _data.RemoveAt(pozice);
                }
            }
        }

        private int NajitSkupinu(SkupinaNecislovanehoNaradi hledana)
        {
            var zakladniPozice = _data.BinarySearch(hledana, _comparer);
            if (zakladniPozice < 0)
                return zakladniPozice;
            for (int pozice = zakladniPozice; pozice >= 0; pozice--)
            {
                var aktualni = _data[pozice];
                if (hledana.Odpovida(aktualni))
                    return pozice;
                else if (_comparer.Compare(aktualni, hledana) != 0)
                    break;
            }
            for (int pozice = zakladniPozice + 1; pozice < _data.Count; pozice++)
            {
                var aktualni = _data[pozice];
                if (hledana.Odpovida(aktualni))
                    return pozice;
                else if (_comparer.Compare(aktualni, hledana) != 0)
                    break;
            }
            return ~zakladniPozice;
        }

        public IList<SkupinaNecislovanehoNaradi> Pouzit(int pocet)
        {
            var vysledek = new List<SkupinaNecislovanehoNaradi>();
            var ziskavanyPocet = Math.Min(pocet, _pocetCelkem);
            foreach (var skupina in _data)
            {
                if (ziskavanyPocet == 0)
                    break;
                else if (skupina.Pocet <= ziskavanyPocet)
                {
                    vysledek.Add(skupina);
                    ziskavanyPocet -= skupina.Pocet;
                }
                else
                {
                    vysledek.Add(skupina.SPoctem(ziskavanyPocet));
                    ziskavanyPocet = 0;
                }
            }
            return vysledek;
        }
    }
}
