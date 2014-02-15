using ServiceLib;
using System;
using System.Collections.Generic;

namespace Vydejna.Contracts
{
    #region Soucasti udalosti
    public class SkupinaNecislovanehoNaradiDto
    {
        public DateTime Datum { get; set; }
        public decimal Cena { get; set; }
        public string Cerstvost { get; set; }
        public int Pocet { get; set; }

        public override int GetHashCode()
        {
            return DtoUtils.GetHashCode(this);
        }
        public override bool Equals(object obj)
        {
            return DtoUtils.Equals(this, obj);
        }
        public override string ToString()
        {
            return DtoUtils.ToString(this);
        }
    }
    #endregion
    #region Obecny presun
    public class NecislovaneNaradiPresunutoEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal? CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public decimal CelkovaCenaPredchozi { get; set; }
        public decimal CelkovaCenaNova { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
        public List<SkupinaNecislovanehoNaradiDto> PouziteKusy { get; set; }
        public List<SkupinaNecislovanehoNaradiDto> NoveKusy { get; set; }
    }
    #endregion
    #region Prijem na vydejnu
    public class NecislovaneNaradiPrijmoutNaVydejnuCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
    }

    public class NecislovaneNaradiPrijatoNaVydejnuEvent : NecislovaneNaradiPresunutoEvent
    {
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
    }
    #endregion
    #region Vydej do vyroby
    public class NecislovaneNaradiVydatDoVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal? CenaNova { get; set; }
        public string KodPracoviste { get; set; }
    }

    public class NecislovaneNaradiVydanoDoVyrobyEvent : NecislovaneNaradiPresunutoEvent
    {
        public string KodPracoviste { get; set; }
    }
    #endregion
    #region Prijem z vyroby
    public class NecislovaneNaradiPrijmoutZVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal? CenaNova { get; set; }
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradi { get; set; }
    }

    public class NecislovaneNaradiPrijatoZVyrobyEvent : NecislovaneNaradiPresunutoEvent
    {
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradi { get; set; }
    }
    #endregion
    #region Predani k oprave
    public class NecislovaneNaradiPredatKOpraveCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal? CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
        public TypOpravy TypOpravy { get; set; }
    }

    public class NecislovaneNaradiPredanoKOpraveEvent : NecislovaneNaradiPresunutoEvent
    {
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
        public TypOpravy TypOpravy { get; set; }
    }
    #endregion
    #region Prijem z opravy
    public class NecislovaneNaradiPrijmoutZOpravyCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
        public decimal? CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public string DodaciList { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradiPoOprave Opraveno { get; set; }
    }

    public class NecislovaneNaradiPrijatoZOpravyEvent : NecislovaneNaradiPresunutoEvent
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
    public class NecislovaneNaradiPredatKeSesrotovaniCommand
    {
        public Guid NaradiId { get; set; }
        public int Pocet { get; set; }
    }

    public class NecislovaneNaradiPredanoKeSesrotovaniEvent : NecislovaneNaradiPresunutoEvent
    {
    }
    #endregion
    #region
    public class NecislovaneNaradiSnapshot_v1
    {
        public List<NecislovaneNaradiSnapshot_v1_Rozlozeni> Rozlozeni { get; set; }
        public int Version { get; set; }
    }
    public class NecislovaneNaradiSnapshot_v1_Rozlozeni
    {
        public UmisteniNaradiDto Umisteni { get; set; }
        public int PocetCelkem { get; set; }
        public List<SkupinaNecislovanehoNaradiDto> Skupiny { get; set; }
    }
    #endregion
    #region Registrace typu
    public class NecislovaneNaradiTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<NecislovaneNaradiPrijmoutNaVydejnuCommand>();
            mapper.Register<NecislovaneNaradiPrijatoNaVydejnuEvent>();
            mapper.Register<NecislovaneNaradiVydatDoVyrobyCommand>();
            mapper.Register<NecislovaneNaradiVydanoDoVyrobyEvent>();
            mapper.Register<NecislovaneNaradiPrijmoutZVyrobyCommand>();
            mapper.Register<NecislovaneNaradiPrijatoZVyrobyEvent>();
            mapper.Register<NecislovaneNaradiPredatKOpraveCommand>();
            mapper.Register<NecislovaneNaradiPredanoKOpraveEvent>();
            mapper.Register<NecislovaneNaradiPrijmoutZOpravyCommand>();
            mapper.Register<NecislovaneNaradiPrijatoZOpravyEvent>();
            mapper.Register<NecislovaneNaradiPredatKeSesrotovaniCommand>();
            mapper.Register<NecislovaneNaradiPredanoKeSesrotovaniEvent>();
            mapper.Register<SkupinaNecislovanehoNaradiDto>();
        }
    }
    #endregion
}
