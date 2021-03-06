﻿using ServiceLib;
using System;
using System.Collections.Generic;

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
        public int PocetCelkem { get; set; }
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

    public class ZiskatNaradiNaVydejneRequest {
        public int Stranka { get; set; }
    }
    public class ZiskatNaradiNaVydejneResponse
    {
        public int Stranka { get; set; }
        public int PocetStranek { get; set; }
        public int PocetCelkem { get; set; }
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

    public enum PrehledObjednavekRazeni
    {
        PodleDataObjednani,
        PodleCislaObjednavky
    }
    public class PrehledObjednavekRequest
    {
        public PrehledObjednavekRazeni Razeni { get; set; }
        public int Stranka { get; set; }
    }
    public class PrehledObjednavekResponse
    {
        public PrehledObjednavekRazeni Razeni { get; set; }
        public int Stranka { get; set; }
        public int PocetStranek { get; set; }
        public int PocetCelkem { get; set; }
        public List<ObjednavkaVPrehledu> Seznam { get; set; }
    }
    public class ObjednavkaVPrehledu
    {
        public DateTime DatumObjednani { get; set; }
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
        public string Objednavka { get; set; }
        public int PocetObjednanych { get; set; }
        public int PocetOpravenych { get; set; }
        public int PocetNeopravitelnych { get; set; }
        public DateTime TerminDodani { get; set; }
    }

    public class PrehledCislovanehoNaradiRequest
    {
        public int Stranka { get; set; }
    }
    public class PrehledCislovanehoNaradiResponse
    {
        public int Stranka { get; set; }
        public int PocetStranek { get; set; }
        public int PocetCelkem { get; set; }
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
