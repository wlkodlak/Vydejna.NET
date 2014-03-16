using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class MnozinaNecislovanehoNaradi
    {
        private List<SkupinaNecislovanehoNaradi> _data, _pridane, _odebrane;
        private int _pocetCelkem;
        private IComparer<SkupinaNecislovanehoNaradi> _comparer;
        private bool _normalizovano;
        private bool _isnull;

        private static MnozinaNecislovanehoNaradi _nullObject = new MnozinaNecislovanehoNaradi { _isnull = true };
        public static MnozinaNecislovanehoNaradi NullObject { get { return _nullObject; } }

        public MnozinaNecislovanehoNaradi()
        {
            _data = new List<SkupinaNecislovanehoNaradi>();
            _pridane = new List<SkupinaNecislovanehoNaradi>();
            _odebrane = new List<SkupinaNecislovanehoNaradi>();
            _pocetCelkem = 0;
            _comparer = new SkupinaNecislovanehoNaradiComparer();
        }

        public int PocetCelkem
        {
            get { return _pocetCelkem; }
            set { if (!_isnull) _pocetCelkem = value; }
        }

        public IList<SkupinaNecislovanehoNaradi> Obsah()
        {
            Normalizovat();
            return _data;
        }

        public void Pridat(SkupinaNecislovanehoNaradi skupina)
        {
            if (_isnull)
                return;
            _normalizovano = false;
            _pocetCelkem += skupina.Pocet;
            _pridane.Add(skupina);
        }

        public void Odebrat(SkupinaNecislovanehoNaradi skupina)
        {
            if (_isnull)
                return;
            _pocetCelkem -= skupina.Pocet;
            if (_pocetCelkem <= 0)
            {
                _normalizovano = true;
                _pocetCelkem = 0;
                _data.Clear();
                _pridane.Clear();
                _odebrane.Clear();
            }
            else
            {
                _normalizovano = false;
                _odebrane.Add(skupina);
            }
        }

        public IList<SkupinaNecislovanehoNaradi> Pouzit(int pocet)
        {
            if (_isnull)
                return new SkupinaNecislovanehoNaradi[0];
            Normalizovat();
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

        private int _normPozicePridane, _normPoziceOdebrane;
        private SkupinaNecislovanehoNaradi _normVysledna, _normZpracovavana;
        private bool _normOdebirani;
        private int _normPocetVysledne;

        private void Normalizovat()
        {
            if (_normalizovano)
                return;
            PripravitNormalizaci();
            while (PosunNormalizace())
                ZpracovatSkupinuNormalizace();
            DokoncitNormalizaci();
        }

        private void PripravitNormalizaci()
        {
            _pridane.AddRange(_data);
            _data.Clear();
            _pridane.Sort(_comparer);
            _odebrane.Sort(_comparer);
            _normPozicePridane = 0;
            _normPoziceOdebrane = 0;
            _normVysledna = null;
        }

        private bool PosunNormalizace()
        {
            if (_normPozicePridane == _pridane.Count)
            {
                if (_normPoziceOdebrane == _odebrane.Count)
                    return false;
                else
                {
                    _normZpracovavana = _odebrane[_normPoziceOdebrane];
                    _normOdebirani = true;
                    _normPoziceOdebrane++;
                    return true;
                }
            }
            else
            {
                if (_normPoziceOdebrane == _odebrane.Count)
                {
                    _normZpracovavana = _pridane[_normPozicePridane];
                    _normOdebirani = false;
                    _normPozicePridane++;
                    return true;
                }
                else
                {
                    var pridavana = _pridane[_normPozicePridane];
                    var odebrana = _odebrane[_normPoziceOdebrane];
                    var porovnani = _comparer.Compare(pridavana, odebrana);
                    if (porovnani <= 0)
                    {
                        _normZpracovavana = pridavana;
                        _normOdebirani = false;
                        _normPozicePridane++;
                        return true;
                    }
                    else
                    {
                        _normZpracovavana = odebrana;
                        _normOdebirani = true;
                        _normPoziceOdebrane++;
                        return true;
                    }
                }
            }
        }

        private void ZpracovatSkupinuNormalizace()
        {
            if (_normVysledna == null)
            {
                _normVysledna = _normZpracovavana;
                _normPocetVysledne = _normOdebirani ? 0 : _normZpracovavana.Pocet;
            }
            else if (_normVysledna.Odpovida(_normZpracovavana))
            {
                if (!_normOdebirani)
                    _normPocetVysledne += _normZpracovavana.Pocet;
                else
                    _normPocetVysledne -= _normZpracovavana.Pocet;
            }
            else
            {
                if (_normPocetVysledne != 0)
                {
                    if (_normVysledna.Pocet != _normPocetVysledne)
                        _data.Add(_normVysledna.SPoctem(_normPocetVysledne));
                    else
                        _data.Add(_normVysledna);
                }

                _normVysledna = _normZpracovavana;
                _normPocetVysledne = _normOdebirani ? 0 : _normZpracovavana.Pocet;
            }
        }

        private void DokoncitNormalizaci()
        {
            if (_normVysledna != null && _normPocetVysledne != 0)
            {
                if (_normVysledna.Pocet != _normPocetVysledne)
                    _data.Add(_normVysledna.SPoctem(_normPocetVysledne));
                else
                    _data.Add(_normVysledna);
            }
            _pridane.Clear();
            _odebrane.Clear();
            _normalizovano = true;
        }

        private class SkupinaNecislovanehoNaradiComparer : IComparer<SkupinaNecislovanehoNaradi>
        {
            private int[] _poradi;

            public SkupinaNecislovanehoNaradiComparer()
            {
                _poradi = new int[3];
                _poradi[(int)CerstvostNecislovanehoNaradi.Pouzite] = 0;
                _poradi[(int)CerstvostNecislovanehoNaradi.Opravene] = 1;
                _poradi[(int)CerstvostNecislovanehoNaradi.Nove] = 2;
            }

            public int Compare(SkupinaNecislovanehoNaradi x, SkupinaNecislovanehoNaradi y)
            {
                int compare;
                compare = DateTime.Compare(x.DatumCerstvosti, y.DatumCerstvosti);
                if (compare != 0)
                    return compare;
                compare = PorovnatCerstvost(x.Cerstvost, y.Cerstvost);
                if (compare != 0)
                    return compare;
                compare = decimal.Compare(x.Cena, y.Cena);
                if (compare != 0)
                    return compare;
                return 0;
            }

            private int PorovnatCerstvost(CerstvostNecislovanehoNaradi x, CerstvostNecislovanehoNaradi y)
            {
                var px = _poradi[(int)x];
                var py = _poradi[(int)y];
                return px == py ? 0 : px < py ? -1 : 1;
            }
        }
    }

    public static class SkupinaNecislovanehoNaradiExtenstions
    {
        public static SkupinaNecislovanehoNaradi ToValue(this SkupinaNecislovanehoNaradiDto dto)
        {
            return SkupinaNecislovanehoNaradi.Dto(dto);
        }
    }
}
