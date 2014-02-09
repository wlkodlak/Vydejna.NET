using ServiceLib;

namespace Vydejna.Contracts
{
    #region Pomocne ciselniky
    public enum StavNaradi
    {
        Neurcen,
        VPoradku,
        NutnoOpravit,
        Neopravitelne
    }

    public enum StavNaradiPoOprave
    {
        Neurcen,
        OpravaNepotrebna,
        Opraveno,
        Neopravitelne
    }

    public enum TypOpravy
    {
        Zadna,
        Oprava,
        Reklamace
    }

    public enum ZakladUmisteni
    {
        VeSkladu,
        NaVydejne,
        VeVyrobe,
        Spotrebovano,
        VeSrotu,
        VOprave
    }

    public class UmisteniNaradiDto
    {
        public ZakladUmisteni ZakladniUmisteni { get; set; }
        public string UpresneniZakladu { get; set; }
        public string Pracoviste { get; set; }
        public string Dodavatel { get; set; }
        public string Objednavka { get; set; }

        public override int GetHashCode()
        {
            return DtoUtils.GetHashCode<UmisteniNaradiDto>(this);
        }
        public override bool Equals(object obj)
        {
            return DtoUtils.Equals<UmisteniNaradiDto>(this, obj);
        }
        public override string ToString()
        {
            return DtoUtils.ToString<UmisteniNaradiDto>(this);
        }
    }
    #endregion
  
}
