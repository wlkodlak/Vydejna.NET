using System;
using System.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class UmisteniNaradi
    {
        private readonly ZakladUmisteni _zakladUmisteni;
        private StavNaradi _stavNaradi;
        private TypOpravy _typOpravy;
        private string _pracoviste;
        private string _dodavatel;
        private string _objednavka;

        private UmisteniNaradi(ZakladUmisteni zaklad)
        {
            _zakladUmisteni = zaklad;
        }
        public static UmisteniNaradi VeSkladu()
        {
            return new UmisteniNaradi(ZakladUmisteni.VeSkladu);
        }
        public static UmisteniNaradi VeSrotu()
        {
            return new UmisteniNaradi(ZakladUmisteni.VeSrotu);
        }
        public static UmisteniNaradi NaVydejne(StavNaradi stav)
        {
            return new UmisteniNaradi(ZakladUmisteni.NaVydejne)
            {
                _stavNaradi = stav
            };
        }
        public static UmisteniNaradi NaOprave(TypOpravy typ, string dodavatel, string objednavka)
        {
            return new UmisteniNaradi(ZakladUmisteni.VOprave)
            {
                _typOpravy = typ,
                _dodavatel = dodavatel,
                _objednavka = objednavka
            };
        }
        public static UmisteniNaradi NaPracovisti(string pracoviste)
        {
            return new UmisteniNaradi(ZakladUmisteni.VeVyrobe)
            {
                _pracoviste = pracoviste
            };
        }
        public static UmisteniNaradi Spotrebovano(string pracoviste)
        {
            return new UmisteniNaradi(ZakladUmisteni.Spotrebovano)
            {
                _pracoviste = pracoviste
            };
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((int)_zakladUmisteni << 25) | ((int)_stavNaradi << 20) | ((int)_typOpravy << 18);
                if (_pracoviste != null)
                    hash = hash ^ _pracoviste.GetHashCode();
                if (_objednavka != null)
                    hash = hash ^ _objednavka.GetHashCode();
                if (_dodavatel != null)
                    hash = hash ^ _dodavatel.GetHashCode();
                return hash;
            }
        }
        public override bool Equals(object obj)
        {
            return Equals(obj as UmisteniNaradi);
        }
        public bool Equals(UmisteniNaradi oth)
        {
            return oth != null
                && _zakladUmisteni == oth._zakladUmisteni
                && _typOpravy == oth._typOpravy
                && _stavNaradi == oth._stavNaradi
                && string.Equals(_pracoviste, oth._pracoviste, StringComparison.Ordinal)
                && string.Equals(_objednavka, oth._objednavka, StringComparison.Ordinal)
                && string.Equals(_dodavatel, oth._dodavatel, StringComparison.Ordinal);
        }
        public override string ToString()
        {
            switch (_zakladUmisteni)
            {
                case ZakladUmisteni.VeSkladu:
                    return "Ve skladu";
                case ZakladUmisteni.VeSrotu:
                    return "Ve srotu";
                case ZakladUmisteni.NaVydejne:
                    switch (_stavNaradi)
                    {
                        default:
                            return "Na vydejne";
                        case StavNaradi.VPoradku:
                            return "Na vydejne v poradku";
                        case StavNaradi.NutnoOpravit:
                            return "Na vydejne k oprave";
                        case StavNaradi.NutnoReklamovat:
                            return "Na vydejne k reklamaci";
                        case StavNaradi.Neopravitelne:
                            return "Na vydejne k sesrotovani";
                    }
                case ZakladUmisteni.VeVyrobe:
                    return string.Concat("Ve vyrobe ", _pracoviste);
                case ZakladUmisteni.Spotrebovano:
                    return string.Concat("Spotrebovano ", _pracoviste);
                case ZakladUmisteni.VOprave:
                    return string.Concat(
                        _typOpravy == TypOpravy.Oprava ? "V oprave u " : "Na reklamaci u ",
                        _dodavatel,
                        " #",
                        _objednavka);
                default:
                    return "Neurcene umisteni";
            }
        }

        public static UmisteniNaradi Dto(UmisteniNaradiDto dto)
        {
            switch (dto.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    return NaVydejne(DekodovatEnum<StavNaradi>(dto.UpresneniZakladu, StavNaradi.Neurcen));
                case ZakladUmisteni.Spotrebovano:
                    return Spotrebovano(dto.Pracoviste);
                case ZakladUmisteni.VeSkladu:
                    return VeSkladu();
                case ZakladUmisteni.VeSrotu:
                    return VeSrotu();
                case ZakladUmisteni.VeVyrobe:
                    return NaPracovisti(dto.Pracoviste);
                case ZakladUmisteni.VOprave:
                    return NaOprave(DekodovatEnum<TypOpravy>(dto.UpresneniZakladu, TypOpravy.Oprava), dto.Dodavatel, dto.Objednavka);
                default:
                    return VeSkladu();
            }
        }

        private static T DekodovatEnum<T>(string hodnota, T defaultValue) where T : struct
        {
            T result;
            if (string.IsNullOrEmpty(hodnota))
                return defaultValue;
            else if (Enum.TryParse<T>(hodnota, out result))
                return result;
            else
                return defaultValue;
        }

        public UmisteniNaradiDto Dto()
        {
            return new UmisteniNaradiDto
            {
                ZakladniUmisteni = _zakladUmisteni,
                Dodavatel = _dodavatel,
                Objednavka = _objednavka,
                Pracoviste = _pracoviste,
                UpresneniZakladu = UpresneniZakladu()
            };
        }
        private string UpresneniZakladu()
        {
            switch (_zakladUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    return _stavNaradi.ToString();
                case ZakladUmisteni.VOprave:
                    return _typOpravy.ToString();
                default:
                    return null;
            }
        }
    }
}
