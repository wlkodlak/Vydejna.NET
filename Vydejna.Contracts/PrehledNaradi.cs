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
    }
    public class PrehledNaradiResponse
    {
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

    public class PrehledNaradiTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
        {
            mapper.Register<PrehledNaradiResponse>();
        }
    }
}
