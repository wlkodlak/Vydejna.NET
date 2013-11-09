using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public interface IReadSeznamNaradi
    {
        Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet);
        Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer);
    }

    public interface IWriteSeznamNaradi
    {
        Task AktivovatNaradi(AktivovatNaradiCommand cmd);
        Task DeaktivovatNaradi(DeaktivovatNaradiCommand cmd);
        Task DefinovatNaradi(DefinovatNaradiCommand definovatNaradiCommand);
    }

    public class SeznamNaradiDto
    {
        public int Offset { get; set; }
        public int PocetCelkem { get; set; }
        public List<TypNaradiDto> SeznamNaradi { get; set; }

        public SeznamNaradiDto()
        {
            this.Offset = 0;
            this.PocetCelkem = 0;
            this.SeznamNaradi = new List<TypNaradiDto>();
        }
    }

    public class TypNaradiDto
    {
        public Guid Id { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }

        public TypNaradiDto(Guid id, string vykres, string rozmer, string druh, bool aktivni)
        {
            this.Id = id;
            this.Vykres = vykres;
            this.Rozmer = rozmer;
            this.Druh = druh;
            this.Aktivni = aktivni;
        }
    }

    public class OvereniUnikatnostiDto
    {
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public bool Existuje { get; set; }
    }

    public class AktivovatNaradiCommand
    {
        public Guid NaradiId;
    }

    public class DeaktivovatNaradiCommand
    {
        public Guid NaradiId;
    }

    public class DefinovatNaradiCommand
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class DefinovatNaradiInternalCommand
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class DokoncitDefiniciNaradiInternalCommand
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class DefinovanoNaradiEvent
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class AktivovanoNaradiEvent
    {
        public Guid NaradiId;
    }

    public class DeaktivovanoNaradiEvent
    {
        public Guid NaradiId;
    }

    public class ZahajenaDefiniceNaradiEvent
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class DokoncenaDefiniceNaradiEvent
    {
        public Guid NaradiId;
        public string Vykres;
        public string Rozmer;
        public string Druh;
    }

    public class ZahajenaAktivaceNaradiEvent
    {
        public Guid NaradiId;
    }
}
