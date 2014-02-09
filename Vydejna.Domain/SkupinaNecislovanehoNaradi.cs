using System;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public enum CerstvostNecislovanehoNaradi
    {
        Nove,
        Opravene,
        Pouzite
    }

    public class SkupinaNecislovanehoNaradi
    {
        public SkupinaNecislovanehoNaradi(DateTime datum, decimal cena, CerstvostNecislovanehoNaradi cerstvost, int pocet)
        {
            DatumCerstvosti = datum;
            Cena = cena;
            Cerstvost = cerstvost;
            Pocet = pocet;
        }

        public DateTime DatumCerstvosti { get; private set; }
        public decimal Cena { get; private set; }
        public CerstvostNecislovanehoNaradi Cerstvost { get; private set; }
        public int Pocet { get; private set; }

        public SkupinaNecislovanehoNaradi Pridat(int pocet)
        {
            return new SkupinaNecislovanehoNaradi(DatumCerstvosti, Cena, Cerstvost, Pocet + pocet);
        }

        public SkupinaNecislovanehoNaradi Odebrat(int pocet)
        {
            return new SkupinaNecislovanehoNaradi(DatumCerstvosti, Cena, Cerstvost, Math.Max(Pocet - pocet, 0));
        }

        public bool Odpovida(SkupinaNecislovanehoNaradi oth)
        {
            return oth != null && DatumCerstvosti == oth.DatumCerstvosti && Cena == oth.Cena && Cerstvost == oth.Cerstvost;
        }

        public override bool Equals(object obj)
        {
            var oth = obj as SkupinaNecislovanehoNaradi;
            return Odpovida(oth) && Pocet == oth.Pocet;
        }

        public override int GetHashCode()
        {
            return DatumCerstvosti.GetHashCode() ^ Cena.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{{ datum: '{0:yyyy-MM-dd hh:mm:ss}', cena: {1}, cerstvost: {2}, pocet: {3} }}", DatumCerstvosti, Cena, Cerstvost, Pocet);
        }

        public SkupinaNecislovanehoNaradi SPoctem(int pocet)
        {
            if (Pocet == pocet)
                return this;
            else
                return new SkupinaNecislovanehoNaradi(DatumCerstvosti, Cena, Cerstvost, pocet);
        }

        public SkupinaNecislovanehoNaradiDto Dto()
        {
            return new SkupinaNecislovanehoNaradiDto
            {
                Datum = DatumCerstvosti,
                Cena = Cena,
                Cerstvost = Cerstvost.ToString(),
                Pocet = Pocet
            };
        }

        public static SkupinaNecislovanehoNaradi Dto(SkupinaNecislovanehoNaradiDto dto)
        {
            return new SkupinaNecislovanehoNaradi(dto.Datum, dto.Cena, CerstvostDto(dto.Cerstvost), dto.Pocet);
        }

        private static CerstvostNecislovanehoNaradi CerstvostDto(string cerstvost)
        {
            CerstvostNecislovanehoNaradi result;
            if (Enum.TryParse<CerstvostNecislovanehoNaradi>(cerstvost, out result))
                return result;
            else
                return CerstvostNecislovanehoNaradi.Pouzite;
        }
    }
}
