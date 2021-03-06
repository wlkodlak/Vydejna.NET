﻿using ServiceLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vydejna.Contracts
{
    public class ZiskatSeznamNaradiRequest
    {
        public ZiskatSeznamNaradiRequest() { }

        public ZiskatSeznamNaradiRequest(int stranka)
        {
            this.Stranka = stranka;
        }

        public int Stranka { get; set; }

        public const int VelikostStranky = 100;
    }

    public class ZiskatSeznamNaradiResponse
    {
        public int Stranka { get; set; }
        public int PocetStranek { get; set; }
        public int PocetCelkem { get; set; }
        public List<TypNaradiDto> SeznamNaradi { get; set; }

        public ZiskatSeznamNaradiResponse()
        {
            this.Stranka = 0;
            this.PocetStranek = 0;
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

        public TypNaradiDto() { }

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

        public OvereniUnikatnostiRequest() { }

        public OvereniUnikatnostiRequest(string vykres, string rozmer)
        {
            this.Vykres = vykres;
            this.Rozmer = rozmer;
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
        public Guid NaradiId { get; set; }
    }

    public class DeaktivovatNaradiCommand
    {
        public Guid NaradiId { get; set; }
    }

    public class DefinovatNaradiCommand
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class DefinovatNaradiInternalCommand
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class DokoncitDefiniciNaradiInternalCommand
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class DefinovanoNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class AktivovanoNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
    }

    public class DeaktivovanoNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
    }

    public class ZahajenaDefiniceNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class DokoncenaDefiniceNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class ZahajenaAktivaceNaradiEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
    }

    public enum TypZmenyNaSklade
    {
        ZvysitStav, SnizitStav, NastavitPresne
    }

    public enum ZdrojZmenyNaSklade
    {
        Manualne, PrijemNaVydejnu, StornoPrijmu
    }

    public class ZmenitStavNaSkladeCommand
    {
        public Guid NaradiId { get; set; }
        public TypZmenyNaSklade TypZmeny { get; set; }
        public int Hodnota { get; set; }
    }

    public class ZmenitStavNaSkladeInternalCommand
    {
        public Guid NaradiId { get; set; }
        public TypZmenyNaSklade TypZmeny { get; set; }
        public int Hodnota { get; set; }
    }

    public class NastalaPotrebaUpravitStavNaSkladeEvent
    {
        public Guid NaradiId { get; set; }
        public int CisloNaradi { get; set; }
        public int Verze { get; set; }
        public TypZmenyNaSklade TypZmeny { get; set; }
        public int Hodnota { get; set; }
    }

    public class ZmenenStavNaSkladeEvent
    {
        public Guid NaradiId { get; set; }
        public int Verze { get; set; }
        public TypZmenyNaSklade TypZmeny { get; set; }
        public ZdrojZmenyNaSklade ZdrojZmeny { get; set; }
        public int Hodnota { get; set; }
        public DateTime DatumZmeny { get; set; }
        public int NovyStav { get; set; }
    }

    public class InformaceONaradi
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class SeznamNaradiTypeMapping : IRegisterTypes
    {
        public void Register(TypeMapper mapper)
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
            mapper.Register<ZmenitStavNaSkladeCommand>();
            mapper.Register<ZmenitStavNaSkladeInternalCommand>();

            mapper.Register<DefinovanoNaradiEvent>();
            mapper.Register<AktivovanoNaradiEvent>();
            mapper.Register<DeaktivovanoNaradiEvent>();
            mapper.Register<ZahajenaDefiniceNaradiEvent>();
            mapper.Register<DokoncenaDefiniceNaradiEvent>();
            mapper.Register<ZahajenaAktivaceNaradiEvent>();
            mapper.Register<ZmenenStavNaSkladeEvent>();
            mapper.Register<NastalaPotrebaUpravitStavNaSkladeEvent>();
        }
    }
}
