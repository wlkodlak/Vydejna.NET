using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class ZiskatNaradiNaPracovistiRequest
    {
        public string KodPracoviste { get; set; }
    }
    public class ZiskatNaradiNaPracovistiResponse
    {
        public bool PracovisteExistuje { get; set; }
        public InformaceOPracovisti Pracoviste { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaPracovisti> Seznam { get; set; }
    }
    public class NaradiNaPracovisti
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public int PocetCelkem { get; set; }
        public int PocetNecislovanych { get; set; }
        public List<int> SeznamCislovanych { get; set; }
        public DateTime? DatumPoslednihoVydeje { get; set; }
    }

    public class ZiskatNaradiNaObjednavceRequest
    {
        public string KodDodavatele { get; set; }
        public string Objednavka { get; set; }
    }
    public class ZiskatNaradiNaObjednavceResponse
    {
        public bool ObjednavkaExistuje { get; set; }
        public InformaceODodavateli Dodavatel { get; set; }
        public string Objednavka { get; set; }
        public DateTime? TerminDodani { get; set; }
        public List<NaradiNaObjednavce> Seznam { get; set; }
    }
    public class NaradiNaObjednavce
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public int PocetCelkem { get; set; }
        public int PocetNecislovanych { get; set; }
        public List<int> SeznamCislovanych { get; set; }
    }

    public class ZiskatNaradiNaVydejneRequest { }
    public class ZiskatNaradiNaVydejneResponse
    {
        public List<NaradiNaVydejne> Seznam { get; set; }
    }
    public class NaradiNaVydejne
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public StavNaradi StavNaradi { get; set; }
        public int PocetCelkem { get; set; }
        public int PocetNecislovanych { get; set; }
        public List<int> SeznamCislovanych { get; set; }
    }

    public class PrehledObjednavekRequest { }
    public class PrehledObjednavekResponse
    {
        public List<ObjednavkaVPrehledu> Seznam { get; set; }
    }
    public class ObjednavkaVPrehledu
    {
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public string Objednavka { get; set; }
        public int PocetObjednanych { get; set; }
        public int PocetOpravenych { get; set; }
        public int PocetNeopravitelnych { get; set; }
        public DateTime TerminDodani { get; set; }
    }

    public class PrehledCislovanehoNaradiRequest { }
    public class PrehledCislovanehoNaradiResponse
    {
        public List<CislovaneNaradiVPrehledu> Seznam { get; set; }
    }
    public class CislovaneNaradiVPrehledu
    {
        public int CisloNaradi { get; set; }
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public UmisteniNaradiDto Umisteni { get; set; }
        public decimal? Cena { get; set; }
    }

    public class NaradiNaUmisteniTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<ZiskatNaradiNaPracovistiResponse>();
            mapper.Register<ZiskatNaradiNaObjednavceResponse>();
            mapper.Register<ZiskatNaradiNaVydejneResponse>();
            mapper.Register<PrehledObjednavekResponse>();
            mapper.Register<PrehledCislovanehoNaradiResponse>();
        }
    }
}
