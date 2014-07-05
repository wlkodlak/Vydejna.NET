using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class PrehledNaradiRequest
    {
        public int Stranka { get; set; }
        public const int VelikostStranky = 100;
    }
    public class PrehledNaradiResponse
    {
        public int Stranka { get; set; }
        public int PocetStranek { get; set; }
        public int PocetCelkem { get; set; }
        public List<PrehledNaradiPolozka> Naradi { get; set; }
    }
    public class PrehledNaradiPolozka
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public int NaSklade { get; set; }
        public int VPoradku { get; set; }
        public int VeVyrobe { get; set; }
        public int Poskozene { get; set; }
        public int Znicene { get; set; }
        public int Opravovane { get; set; }
    }

    public class DetailNaradiRequest
    {
        public Guid NaradiId { get; set; }
    }
    public class DetailNaradiResponse
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }
        public int NaSklade { get; set; }

        public DetailNaradiPocty PoctyCelkem { get; set; }
        public DetailNaradiPocty PoctyNecislovane { get; set; }
        public DetailNaradiPocty PoctyCislovane { get; set; }

        public List<DetailNaradiNecislovane> Necislovane { get; set; }
        public List<DetailNaradiCislovane> Cislovane { get; set; }
    }
    public class DetailNaradiPocty
    {
        public int VPoradku { get; set; }
        public int VOprave { get; set; }
        public int VeVyrobe { get; set; }
        public int Poskozene { get; set; }
        public int Znicene { get; set; }
    }
    public class DetailNaradiNecislovane
    {
        public ZakladUmisteni ZakladUmisteni { get; set; }
        public int Pocet { get; set; }
        public DetailNaradiVeVyrobe VeVyrobe { get; set; }
        public DetailNaradiVOprave VOprave { get; set; }
        public DetailNaradiNaVydejne NaVydejne { get; set; }
    }
    public class DetailNaradiCislovane
    {
        public int CisloNaradi { get; set; }
        public ZakladUmisteni ZakladUmisteni { get; set; }
        public DetailNaradiVeVyrobe VeVyrobe { get; set; }
        public DetailNaradiVOprave VOprave { get; set; }
        public DetailNaradiNaVydejne NaVydejne { get; set; }
    }
    public class DetailNaradiVeVyrobe
    {
        public string KodPracoviste { get; set; }
        public string NazevPracoviste { get; set; }
        public string StrediskoPracoviste { get; set; }
    }
    public class DetailNaradiVOprave
    {
        public TypOpravy TypOpravy { get; set; }
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public string Objednavka { get; set; }
        public DateTime TerminDodani { get; set; }
    }
    public class DetailNaradiNaVydejne
    {
        public StavNaradi StavNaradi { get; set; }
        public string KodVady { get; set; }
        public string NazevVady { get; set; }
    }
    public enum HistorieNaradiTypFiltru
    {
        Vsechno,
        CislovaneNaradi,
        Objednavka,
        Pracoviste,
        Naradi
    }
    public class HistorieNaradiRequest
    {
        public HistorieNaradiTypFiltru TypFiltru { get; set; }
        public Guid? NaradiId { get; set; }
        public DateTime? DatumOd { get; set; }
        public DateTime? DatumDo { get; set; }
        public string KodDodavatele { get; set; }
        public string CisloObjednavky { get; set; }
        public string KodPracoviste { get; set; }
        public bool PouzeVydejeDoVyroby { get; set; }
        public int? CisloNaradi { get; set; }
        public int Stranka { get; set; }
    }
    public class HistorieNaradiResponse
    {
        public HistorieNaradiRequest Filtr { get; set; }
        public int PocetCelkem { get; set; }
        public int PocetStranek { get; set; }
        public List<HistorieNaradiOperace> SeznamOperaci { get; set; }
    }
    public class HistorieNaradiOperace
    {
        public Guid EventId { get; set; }
        public DateTime Datum { get; set; }
        public int? CisloNaradi { get; set; }
        public int Pocet { get; set; }
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string KodPracoviste { get; set; }
        public string NazevPracoviste { get; set; }
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public string CisloObjednavky { get; set; }
        public string TypUdalosti { get; set; }
        public string NazevOperace { get; set; }
        public string KodVady { get; set; }
        public string NazevVady { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public decimal? PuvodniCelkovaCena { get; set; }
        public decimal? NovaCelkovaCena { get; set; }
        public bool Stornovano { get; set; }
    }

    public class PrehledNaradiTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<PrehledNaradiResponse>();
            mapper.Register<DetailNaradiResponse>();
            mapper.Register<HistorieNaradiResponse>();
        }
    }
}
