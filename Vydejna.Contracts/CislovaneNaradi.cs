using ServiceLib;
using System;

namespace Vydejna.Contracts
{
    #region Prijem na vydejnu
    public class CislovaneNaradiPrijmoutNaVydejnuCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
    }

    public class CislovaneNaradiPrijatoNaVydejnuEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public string KodDodavatele { get; set; }
        public bool PrijemZeSkladu { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }

    public class CislovaneNaradiStornovatPrijemNaVydejnuCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanPrijemNaVydejnuEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public DateTime DatumStorna { get; set; }
        public bool PrijemZeSkladu { get; set; }
        public Guid StornovanaUdalost { get; set; }
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

    public class CislovaneNaradiVydanoDoVyrobyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredchozi { get; set; }
        public decimal CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public string KodPracoviste { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }

    public class CislovaneNaradiStornovatVydaniDoVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanoVydaniDoVyrobyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPoVydani { get; set; }
        public decimal CenaPredVydanim { get; set; }
        public DateTime DatumStorna { get; set; }
        public string KodPracoviste { get; set; }
        public Guid StornovanaUdalost { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
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

    public class CislovaneNaradiPrijatoZVyrobyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredchozi { get; set; }
        public decimal CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public string KodVady { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }

    public class CislovaneNaradiStornovatPrijemZVyrobyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanPrijemZVyrobyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPoPrijmu { get; set; }
        public decimal CenaPredPrijmem { get; set; }
        public DateTime DatumStorna { get; set; }
        public string KodPracoviste { get; set; }
        public StavNaradi StavNaradiPoPrijmu { get; set; }
        public string KodVadyPoPrijmu { get; set; }
        public Guid StornovanaUdalost { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
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

    public class CislovaneNaradiPredanoKOpraveEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredchozi { get; set; }
        public decimal CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }

    public class CislovaneNaradiStornovatPredaniKOpraveCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanoPredaniKOpraveEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPoOprave { get; set; }
        public decimal CenaPredOpravou { get; set; }
        public DateTime DatumStorna { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradi StavNaradiPredOpravou { get; set; }
        public Guid StornovanaUdalost { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
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

    public class CislovaneNaradiPrijatoZOpravyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredchozi { get; set; }
        public decimal CenaNova { get; set; }
        public DateTime Datum { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public string DodaciList { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public StavNaradiPoOprave Opraveno { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }
   
    public class CislovaneNaradiStornovatPrijemZOpravyCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanPrijemZOpravyEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPoPrijmu { get; set; }
        public decimal CenaPredPrijmem { get; set; }
        public DateTime DatumStorna { get; set; }
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
        public string DodaciList { get; set; }
        public TypOpravy TypOpravy { get; set; }
        public StavNaradi StavNaradiPoPrijmu { get; set; }
        public Guid StornovanaUdalost { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }
    #endregion
    #region Sesrotovani
    public class CislovaneNaradiPredatKeSesrotovaniCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
    }

    public class CislovaneNaradiPredanoKeSesrotovaniEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredchozi { get; set; }
        public DateTime Datum { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
    }
   
    public class CislovaneNaradiStornovatPredaniKeSesrotovaniCommand
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public Guid StornovanaUdalost { get; set; }
    }

    public class CislovaneNaradiStornovanoPredaniKeSesrotovaniEvent
    {
        public Guid EventId { get; set; }
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public decimal CenaPredPredanim { get; set; }
        public DateTime DatumStorna { get; set; }
        public Guid StornovanaUdalost { get; set; }
        public UmisteniNaradiDto PredchoziUmisteni { get; set; }
        public UmisteniNaradiDto NoveUmisteni { get; set; }
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
            mapper.Register<CislovaneNaradiStornovatPrijemNaVydejnuCommand>();
            mapper.Register<CislovaneNaradiStornovanPrijemNaVydejnuEvent>();

            mapper.Register<CislovaneNaradiVydatDoVyrobyCommand>();
            mapper.Register<CislovaneNaradiVydanoDoVyrobyEvent>();
            mapper.Register<CislovaneNaradiStornovatVydaniDoVyrobyCommand>();
            mapper.Register<CislovaneNaradiStornovanoVydaniDoVyrobyEvent>();

            mapper.Register<CislovaneNaradiPrijmoutZVyrobyCommand>();
            mapper.Register<CislovaneNaradiPrijatoZVyrobyEvent>();
            mapper.Register<CislovaneNaradiStornovatPrijemZVyrobyCommand>();
            mapper.Register<CislovaneNaradiStornovanPrijemZVyrobyEvent>();

            mapper.Register<CislovaneNaradiPredatKOpraveCommand>();
            mapper.Register<CislovaneNaradiPredanoKOpraveEvent>();
            mapper.Register<CislovaneNaradiStornovatPredaniKOpraveCommand>();
            mapper.Register<CislovaneNaradiStornovanoPredaniKOpraveEvent>();

            mapper.Register<CislovaneNaradiPrijmoutZOpravyCommand>();
            mapper.Register<CislovaneNaradiPrijatoZOpravyEvent>();
            mapper.Register<CislovaneNaradiStornovatPrijemZOpravyCommand>();
            mapper.Register<CislovaneNaradiStornovanPrijemZOpravyEvent>();

            mapper.Register<CislovaneNaradiPredatKeSesrotovaniCommand>();
            mapper.Register<CislovaneNaradiPredanoKeSesrotovaniEvent>();
            mapper.Register<CislovaneNaradiStornovatPredaniKeSesrotovaniCommand>();
            mapper.Register<CislovaneNaradiStornovanoPredaniKeSesrotovaniEvent>();
        }
    }
    #endregion
}
