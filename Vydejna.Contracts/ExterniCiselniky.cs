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
    public class ZiskatSeznamDodavateluReponse
    {
        public List<SeznamDodavateluPolozka> Seznam { get; set; }
    }
    public class SeznamDodavateluPolozka
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
        public List<SeznamPracovistPolozka> Seznam { get; set; }
    }
    public class SeznamPracovistPolozka
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
        public string Stredisko { get; set; }
        public bool Aktivni { get; set; }
    }

    public class ExterniCiselnikyTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<DefinovanaVadaNaradiEvent>();
            mapper.Register<ZiskatSeznamVadResponse>();
            mapper.Register<DefinovanDodavatelEvent>();
            mapper.Register<ZiskatSeznamDodavateluReponse>();
            mapper.Register<DefinovanoPracovisteEvent>();
            mapper.Register<ZiskatSeznamPracovistResponse>();
        }
    }
}
