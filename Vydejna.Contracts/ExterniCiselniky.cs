using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class DefinovanaVadaNaradiEvent
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
        public bool Deaktivovana { get; set; }
    }
    public class ZiskatSeznamVadRequest { }
    public class ZiskatSeznamVadResponse
    {
        public List<SeznamVadPolozka> Seznam { get; set; }
    }
    public class SeznamVadPolozka
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
        public bool Aktivni { get; set; }
    }

    public class DefinovanDodavatelEvent
    {
        public string Kod { get; set; }
        public bool Deaktivovan { get; set; }
        public string Nazev { get; set; }
        public string[] Adresa { get; set; }
        public string Ico { get; set; }
        public string Dic { get; set; }
    }
    public class ZiskatSeznamDodavateluRequest { }
    public class ZiskatSeznamDodavateluResponse
    {
        public List<InformaceODodavateli> Seznam { get; set; }
    }
    public class InformaceODodavateli
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
        public string[] Adresa { get; set; }
        public string Ico { get; set; }
        public string Dic { get; set; }
        public bool Aktivni { get; set; }
    }

    public class DefinovanoPracovisteEvent
    {
        public string Kod { get; set; }
        public bool Deaktivovano { get; set; }
        public string Nazev { get; set; }
        public string Stredisko { get; set; }
    }
    public class ZiskatSeznamPracovistRequest { }
    public class ZiskatSeznamPracovistResponse 
    {
        public List<InformaceOPracovisti> Seznam { get; set; }
    }
    public class InformaceOPracovisti
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
        public string Stredisko { get; set; }
        public bool Aktivni { get; set; }
    }

    public class NajitDodaciListRequest
    {
        public string DodaciList { get; set; }
    }
    public class NajitDodaciListResponse
    {
        public string DodaciList { get; set; }
        public bool Nalezen { get; set; }
        public List<NalezenyDodaciList> Kandidati { get; set; }
    }
    public class NalezenyDodaciList
    {
        public string DodaciList { get; set; }
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public List<string> Objednavky { get; set; }
    }

    public class NajitObjednavkuRequest
    {
        public string Objednavka { get; set; }
    }
    public class NajitObjednavkuResponse
    {
        public string Objednavka { get; set; }
        public bool Nalezena { get; set; }
        public List<NalezenaObjednavka> Kandidati { get; set; }
    }
    public class NalezenaObjednavka
    {
        public string Objednavka { get; set; }
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public DateTime TerminDodani { get; set; }
    }

    public class ExterniCiselnikyTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<DefinovanaVadaNaradiEvent>();
            mapper.Register<ZiskatSeznamVadResponse>();
            mapper.Register<DefinovanDodavatelEvent>();
            mapper.Register<ZiskatSeznamDodavateluResponse>();
            mapper.Register<DefinovanoPracovisteEvent>();
            mapper.Register<ZiskatSeznamPracovistResponse>();
            mapper.Register<NajitDodaciListResponse>();
            mapper.Register<NajitObjednavkuResponse>();
        }
    }
}
