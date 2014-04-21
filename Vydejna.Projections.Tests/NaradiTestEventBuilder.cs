using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Vydejna.Contracts;

namespace Vydejna.Projections.Tests
{
    public class ProjectionTestEventBuilder
    {
        private ReadModelTestBase _test;

        public ProjectionTestEventBuilder(ReadModelTestBase test)
        {
            _test = test;
        }

        protected void DefinovanDodavatel(string kod, string nazev)
        {
            _test.SendEvent(new DefinovanDodavatelEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovan = false,
                Ico = kod,
                Dic = kod,
                Adresa = new[] { nazev, "38001 Dacice" }
            });
        }

        protected void DefinovanoPracoviste(string kod, string nazev, string stredisko)
        {
            _test.SendEvent(new DefinovanoPracovisteEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovano = false,
                Stredisko = stredisko
            });
        }

        protected void DefinovanaVada(string kod, string nazev)
        {
            _test.SendEvent(new DefinovanaVadaNaradiEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovana = string.IsNullOrEmpty(nazev)
            });
        }

        public NaradiTestEventBuilder Naradi(string naradi)
        {
            return new NaradiTestEventBuilder(_test, naradi);
        }
    }

    public class NaradiTestEventBuilder
    {
        private ReadModelTestBase _test;
        private Guid _naradiId;
        private int _verzeDefinovaneho;
        private int _verzeNecislovaneho;
        private int _verzeCislovanych;
        
        private int _pocetNaSklade;
        private Dictionary<int, UmisteniNaradiDto> _umisteniCislovanych;
        private Dictionary<string, int> _poctyNecislovanych;

        public Guid NaradiId { get { return _naradiId; } }
        
        public NaradiTestEventBuilder(ReadModelTestBase test, string naradi)
        {
            _test = test;
            _naradiId = BuildNaradiId(naradi);
            _umisteniCislovanych = new Dictionary<int, UmisteniNaradiDto>();
            _poctyNecislovanych = new Dictionary<string, int>();
        }

        public static Guid BuildNaradiId(string naradi)
        {
            switch (naradi)
            {
                case "A": return new Guid("0000000A-3849-55b1-c23d-18be1932bb11");
                case "B": return new Guid("0000000B-3849-55b1-c23d-18be1932bb11");
                case "C": return new Guid("0000000C-3849-55b1-c23d-18be1932bb11");
                case "D": return new Guid("0000000D-3849-55b1-c23d-18be1932bb11");
                default: return new Guid("0000" + naradi + "-3849-55b1-c23d-18be1932bb11");
            }
        }

        public void Definovano(string vykres, string rozmer, string druh)
        {
            _test.SendEvent(new DefinovanoNaradiEvent
            {
                NaradiId = _naradiId,
                Vykres = vykres,
                Rozmer = rozmer,
                Druh = druh,
                Verze = ++_verzeDefinovaneho
            });
        }

        public void Deaktivovano()
        {
            _test.SendEvent(new DeaktivovanoNaradiEvent
            {
                NaradiId = _naradiId,
                Verze = ++_verzeDefinovaneho
            });
        }

        public void Aktivovano()
        {
            _test.SendEvent(new AktivovanoNaradiEvent
            {
                NaradiId = _naradiId,
                Verze = ++_verzeDefinovaneho
            });
        }

        public ZmenaNaSkladeBuilder ZmenaNaSklade()
        {
            return new ZmenaNaSkladeBuilder(this);
        }

        public class ZmenaNaSkladeBuilder
        {
            private ZmenenStavNaSkladeEvent _evnt;
            private NaradiTestEventBuilder _parent;

            public ZmenaNaSkladeBuilder(NaradiTestEventBuilder parent)
            {
                _parent = parent;
                _evnt = new ZmenenStavNaSkladeEvent
                {
                    NaradiId = _parent._naradiId,
                    DatumZmeny = _parent._test.CurrentTime,
                    Verze = ++_parent._verzeDefinovaneho
                };
            }

            public ZmenaNaSkladeBuilder Zdroj(ZdrojZmenyNaSklade zdroj)
            {
                _evnt.ZdrojZmeny = zdroj;
                return this;
            }

            public ZmenaNaSkladeBuilder Zmena(int zmena)
            {
                _evnt.Hodnota = zmena > 0 ? zmena : -zmena;
                _evnt.NovyStav = _parent._pocetNaSklade + zmena;
                _evnt.TypZmeny = zmena > 0 ? TypZmenyNaSklade.ZvysitStav : TypZmenyNaSklade.SnizitStav;
                return this;
            }

            public ZmenaNaSkladeBuilder Absolutne(int novyStav)
            {
                _evnt.Hodnota = novyStav;
                _evnt.NovyStav = novyStav;
                _evnt.TypZmeny = TypZmenyNaSklade.NastavitPresne;
                return this;
            }

            public void Send()
            {
                _parent._pocetNaSklade = _evnt.NovyStav;
                _parent._test.SendEvent(_evnt);
            }
        }

        public PresunNaradiBuilder Prijmout()
        {
            return new PresunNaradiBuilder(this, "Prijem");
        }

        public class PresunNaradiBuilder
        {
            private NaradiTestEventBuilder _parent;
            private string _rezim;

            public PresunNaradiBuilder(NaradiTestEventBuilder parent, string rezim)
            {
                _parent = parent;
                _rezim = rezim;
            }

            public PresunNecislovanehoBuilder Necislovane(int pocet)
            {
                return new PresunNecislovanehoBuilder(_parent, _rezim, pocet);
            }

            public PresunCislovanehoBuilder Cislovane(int cisloNaradi)
            {
                return new PresunCislovanehoBuilder(_parent, _rezim, cisloNaradi);
            }
        }

        public class PresunCislovanehoBuilder
        {
            private NaradiTestEventBuilder _parent;
            private string _rezim;
            private CislovaneNaradiPresunutoEvent _evnt;

            public PresunCislovanehoBuilder(NaradiTestEventBuilder parent, string rezim, int cisloNaradi)
            {
                _parent = parent;
                _rezim = rezim;
                switch (rezim)
                {
                    case "Prijem":
                        _evnt = new CislovaneNaradiPrijatoNaVydejnuEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Dodavatel("D005");
                        break;
                    default:
                        throw new NotSupportedException("Rezim " + _rezim + " nepodporovan");
                }
                _evnt.CenaNova = 0m;
                _evnt.CenaPredchozi = 0m;
                _evnt.CisloNaradi = cisloNaradi;
                _evnt.Datum = _parent._test.CurrentTime;
                _evnt.EventId = Guid.NewGuid();
                _evnt.NaradiId = _parent._naradiId;
                _evnt.Verze = ++_parent._verzeCislovanych;
            }

            public PresunCislovanehoBuilder ZeSkladu()
            {
                var prijem = (CislovaneNaradiPrijatoNaVydejnuEvent)_evnt;
                prijem.PrijemZeSkladu = true;
                return this;
            }

            public PresunCislovanehoBuilder Dodavatel(string dodavatel)
            {
                var prijem = _evnt as CislovaneNaradiPrijatoNaVydejnuEvent;
                if (prijem != null)
                {
                    prijem.KodDodavatele = dodavatel;
                }

                return this;
            }

            public void Send()
            {
                if (_evnt.PredchoziUmisteni == null)
                {
                    _evnt.PredchoziUmisteni = _parent._umisteniCislovanych[_evnt.CisloNaradi];
                }
                _parent._umisteniCislovanych[_evnt.CisloNaradi] = _evnt.NoveUmisteni;
                switch (_rezim)
                {
                    case "Prijem":
                        _parent._test.SendEvent((CislovaneNaradiPrijatoNaVydejnuEvent)_evnt);
                        break;
                }
            }
        }

        public class PresunNecislovanehoBuilder
        {
            private NaradiTestEventBuilder _parent;
            private string _rezim;
            private NecislovaneNaradiPresunutoEvent _evnt;

            public PresunNecislovanehoBuilder(NaradiTestEventBuilder parent, string rezim, int pocet)
            {
                _parent = parent;
                _rezim = rezim;
                switch (rezim)
                {
                    case "Prijem":
                        _evnt = new NecislovaneNaradiPrijatoNaVydejnuEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSkladu };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Dodavatel("D005");
                        break;
                    default:
                        throw new NotSupportedException("Rezim " + _rezim + " nepodporovan");
                }
                _evnt.CenaNova = 0m;
                _evnt.CelkovaCenaNova = 0m;
                _evnt.CelkovaCenaPredchozi = 0m;
                _evnt.PouziteKusy = new List<SkupinaNecislovanehoNaradiDto>();
                _evnt.NoveKusy = new List<SkupinaNecislovanehoNaradiDto>();
                _evnt.Datum = _parent._test.CurrentTime;
                _evnt.EventId = Guid.NewGuid();
                _evnt.NaradiId = _parent._naradiId;
                _evnt.Verze = ++_parent._verzeNecislovaneho;
                _evnt.PocetNaPredchozim = 0;
                _evnt.PocetNaNovem = pocet;
                _evnt.Pocet = pocet;
            }

            public PresunNecislovanehoBuilder ZeSkladu()
            {
                var prijem = (NecislovaneNaradiPrijatoNaVydejnuEvent)_evnt;
                prijem.PrijemZeSkladu = true;
                return this;
            }

            public PresunNecislovanehoBuilder Dodavatel(string dodavatel)
            {
                var prijem = _evnt as NecislovaneNaradiPrijatoNaVydejnuEvent;
                if (prijem != null)
                {
                    prijem.KodDodavatele = dodavatel;
                }

                return this;
            }

            public void Send()
            {
                DoplnitCelkovyPocet();
                switch (_rezim)
                {
                    case "Prijem":
                        Send((NecislovaneNaradiPrijatoNaVydejnuEvent)_evnt);
                        break;
                }
            }

            private static string KlicUmisteni(UmisteniNaradiDto umisteni)
            {
                var sb = new StringBuilder();
                sb.Append(umisteni.ZakladniUmisteni);
                sb.Append("-");
                sb.Append(umisteni.UpresneniZakladu);
                sb.Append("-");
                sb.Append(umisteni.Pracoviste);
                sb.Append("-");
                sb.Append(umisteni.Dodavatel);
                sb.Append("-");
                sb.Append(umisteni.Objednavka);
                return sb.ToString();
            }

            private void DoplnitCelkovyPocet()
            {
                int celkovyPocet;
                string klic;

                klic = KlicUmisteni(_evnt.PredchoziUmisteni);
                _parent._poctyNecislovanych.TryGetValue(klic, out celkovyPocet);
                _evnt.PocetNaPredchozim = Math.Max(0, celkovyPocet - _evnt.Pocet);
                _parent._poctyNecislovanych[klic] = _evnt.PocetNaPredchozim;

                klic = KlicUmisteni(_evnt.NoveUmisteni);
                _parent._poctyNecislovanych.TryGetValue(klic, out celkovyPocet);
                _evnt.PocetNaNovem = celkovyPocet + _evnt.Pocet;
                _parent._poctyNecislovanych[klic] = _evnt.PocetNaNovem;
            }

            private void Send(NecislovaneNaradiPrijatoNaVydejnuEvent evnt)
            {
                _parent._test.SendEvent(evnt);
            }
        }
    }
}
