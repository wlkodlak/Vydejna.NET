using ServiceLib;
using System;

namespace Vydejna.Contracts
{
    #region Obecny presun
    public class CislovaneNaradiPresunutoEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public int Verze { get; set; }
        public DateTime Datum { get; set; }
        public decimal CenaPredchozi { get; set; }
        public decimal CenaNova { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }
    #endregion
    #region Prijem na vydejnu
    public class CislovaneNaradiPrijmoutNaVydejnuCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
    }

    public class CislovaneNaradiPrijatoNaVydejnuEvent : CislovaneNaradiPresunutoEvent
    {
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
    }
    #endregion
    #region Vydej do vyroby
    public class CislovaneNaradiVydatDoVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodPracoviste { get; set; }
    }

    public class CislovaneNaradiVydanoDoVyrobyEvent : CislovaneNaradiPresunutoEvent
    {
        public string KodPracoviste { get; set; }
    }
    #endregion
    #region Prijem z vyroby
    public class CislovaneNaradiPrijmoutZVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public string KodVady { get; set; }
    }

    public class CislovaneNaradiPrijatoZVyrobyEvent : CislovaneNaradiPresunutoEvent
    {
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public string KodVady { get; set; }
    }
    #endregion
    #region Predani k oprave
    public class CislovaneNaradiPredatKOpraveCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
        public TypOpravy TypOpravy { get; set; }
    }

    public class CislovaneNaradiPredanoKOpraveEvent : CislovaneNaradiPresunutoEvent
    {
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
        public TypOpravy TypOpravy { get; set; }
    }
    #endregion
    #region Prijem z opravy
    public class CislovaneNaradiPrijmoutZOpravyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public string DodaciList { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradiPoOprave Opraveno { get; set; }
    }

    public class CislovaneNaradiPrijatoZOpravyEvent : CislovaneNaradiPresunutoEvent
    {
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public string DodaciList { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public StavNaradiPoOprave Opraveno { get; set; }
    }
    #endregion
    #region Sesrotovani
    public class CislovaneNaradiPredatKeSesrotovaniCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
    }

    public class CislovaneNaradiPredanoKeSesrotovaniEvent : CislovaneNaradiPresunutoEvent
    {
    }
    #endregion
    #region Snapshot
    public class CislovaneNaradiSnapshot_v1
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public UmisteniNaradiDto Umisteni { get; set; }
        public decimal Cena { get; set; }
        public int Version { get; set; }
    }
    #endregion
    #region Registrace typu
    public class CislovaneNaradiTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<CislovaneNaradiPrijmoutNaVydejnuCommand>();
            mapper.Register<CislovaneNaradiPrijatoNaVydejnuEvent>();
            mapper.Register<CislovaneNaradiVydatDoVyrobyCommand>();
            mapper.Register<CislovaneNaradiVydanoDoVyrobyEvent>();
            mapper.Register<CislovaneNaradiPrijmoutZVyrobyCommand>();
            mapper.Register<CislovaneNaradiPrijatoZVyrobyEvent>();
            mapper.Register<CislovaneNaradiPredatKOpraveCommand>();
            mapper.Register<CislovaneNaradiPredanoKOpraveEvent>();
            mapper.Register<CislovaneNaradiPrijmoutZOpravyCommand>();
            mapper.Register<CislovaneNaradiPrijatoZOpravyEvent>();
            mapper.Register<CislovaneNaradiPredatKeSesrotovaniCommand>();
            mapper.Register<CislovaneNaradiPredanoKeSesrotovaniEvent>();
        }
    }
    #endregion
}
