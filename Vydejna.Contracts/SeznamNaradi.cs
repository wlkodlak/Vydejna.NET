using ServiceLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vydejna.Contracts
{
    public interface IReadSeznamNaradi
        : IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>
        , IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>
    {
    }

    public interface IWriteSeznamNaradi
        : IHandle<CommandExecution<AktivovatNaradiCommand>>
        , IHandle<CommandExecution<DeaktivovatNaradiCommand>>
        , IHandle<CommandExecution<DefinovatNaradiCommand>>
    {
    }

    public static class VydejnaTypeMapperConfigurator
    {
        public static void Configure(TypeMapper mapper)
        {
            mapper.Register<ZiskatSeznamNaradiRequest>();
            mapper.Register<ZiskatSeznamNaradiResponse>();
            mapper.Register<TypNaradiDto>();
            mapper.Register<OvereniUnikatnostiRequest>();
            mapper.Register<OvereniUnikatnostiResponse>();
            mapper.Register<AktivovatNaradiCommand>();
            mapper.Register<DeaktivovatNaradiCommand>();
            mapper.Register<DefinovatNaradiInternalCommand>();
            mapper.Register<DokoncitDefiniciNaradiInternalCommand>();
            mapper.Register<DefinovanoNaradiEvent>();
            mapper.Register<AktivovanoNaradiEvent>();
            mapper.Register<DeaktivovanoNaradiEvent>();
            mapper.Register<ZahajenaDefiniceNaradiEvent>();
            mapper.Register<DokoncenaDefiniceNaradiEvent>();
            mapper.Register<ZahajenaAktivaceNaradiEvent>();
        }
    }

    public static class DtoUtils
    {
        public static int GetHashCode<T>(T obj)
        {
            unchecked
            {
                var type = typeof(T);
                int hash = 48972847;
                foreach (var property in type.GetProperties())
                {
                    var value = property.GetValue(obj);
                    hash *= 30481;
                    if (value != null)
                        hash += value.GetHashCode();
                }
                return hash;
            }
        }

        public static bool Equals<T>(T a, object b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);
            else if (ReferenceEquals(b, null))
                return false;
            else if (a.GetType() != typeof(T) || b.GetType() != typeof(T))
                return false;
            else
            {
                foreach (var property in typeof(T).GetProperties())
                {
                    var valA = property.GetValue(a);
                    var valB = property.GetValue(b);
                    if (!object.Equals(valA, valB))
                        return false;
                }
                return true;
            }
        }

        public static string ToString<T>(T obj)
        {
            if (ReferenceEquals(obj, null))
                return "null";
            var sb = new StringBuilder();
            sb.Append(typeof(T).Name).Append(" { ");
            bool first = true;
            foreach (var property in typeof(T).GetProperties())
            {
                var value = property.GetValue(obj);
                if (first)
                    first = false;
                else
                    sb.Append(", ");
                sb.Append(property.Name).Append(" = ").Append(value);
            }
            sb.Append(first ? "}" : " }");
            return sb.ToString();
        }
    }

    public class ZiskatSeznamNaradiRequest
    {
        public ZiskatSeznamNaradiRequest(int offset, int pocet)
        {
            this.Offset = offset;
            this.MaxPocet = pocet;
        }
        public int Offset { get; set; }
        public int MaxPocet { get; set; }

        public override bool Equals(object obj)
        {
            return DtoUtils.Equals(this, obj);
        }
        public override int GetHashCode()
        {
            return DtoUtils.GetHashCode(this);
        }
        public override string ToString()
        {
            return DtoUtils.ToString(this);
        }
    }

    public class ZiskatSeznamNaradiResponse
    {
        public int Offset { get; set; }
        public int PocetCelkem { get; set; }
        public List<TypNaradiDto> SeznamNaradi { get; set; }

        public ZiskatSeznamNaradiResponse()
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

    public class OvereniUnikatnostiRequest
    {
        public string Vykres { get; set; }
        public string Rozmer { get; set; }

        public OvereniUnikatnostiRequest(string vykres, string rozmer)
        {
            this.Vykres = vykres;
            this.Rozmer = rozmer;
        }

        public override bool Equals(object obj)
        {
            return DtoUtils.Equals(this, obj);
        }
        public override int GetHashCode()
        {
            return DtoUtils.GetHashCode(this);
        }
        public override string ToString()
        {
            return DtoUtils.ToString(this);
        }
    }

    public class OvereniUnikatnostiResponse
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

    public static class VydejnaContractList
    {
        public static void RegisterTypes(TypeMapper mapper)
        {
            mapper.Register<ZiskatSeznamNaradiRequest>();
            mapper.Register<ZiskatSeznamNaradiResponse>();
            mapper.Register<TypNaradiDto>();
            mapper.Register<OvereniUnikatnostiRequest>();
            mapper.Register<OvereniUnikatnostiResponse>();

            mapper.Register<AktivovatNaradiCommand>();
            mapper.Register<DeaktivovatNaradiCommand>();
            mapper.Register<DefinovatNaradiCommand>();
            mapper.Register<DefinovatNaradiInternalCommand>();
            mapper.Register<DokoncitDefiniciNaradiInternalCommand>();

            mapper.Register<DefinovanoNaradiEvent>();
            mapper.Register<AktivovanoNaradiEvent>();
            mapper.Register<DeaktivovanoNaradiEvent>();
            mapper.Register<ZahajenaDefiniceNaradiEvent>();
            mapper.Register<DokoncenaDefiniceNaradiEvent>();
            mapper.Register<ZahajenaAktivaceNaradiEvent>();
        }
    }
}
