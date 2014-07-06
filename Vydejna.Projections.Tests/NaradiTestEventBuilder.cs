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

        public void Dodavatel(string kod, string nazev)
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

        public void Pracoviste(string kod, string nazev, string stredisko)
        {
            _test.SendEvent(new DefinovanoPracovisteEvent
            {
                Kod = kod,
                Nazev = nazev,
                Deaktivovano = false,
                Stredisko = stredisko
            });
        }

        public void Vada(string kod, string nazev)
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

        public PresunNaradiBuilder Vydat()
        {
            return new PresunNaradiBuilder(this, "Vydej");
        }

        public PresunNaradiBuilder Vratit()
        {
            return new PresunNaradiBuilder(this, "Vraceni");
        }

        public PresunNaradiBuilder DatOpravit()
        {
            return new PresunNaradiBuilder(this, "Odeslani");
        }

        public PresunNaradiBuilder Opravit()
        {
            return new PresunNaradiBuilder(this, "Opraveni");
        }

        public PresunNaradiBuilder Srotovat()
        {
            return new PresunNaradiBuilder(this, "Srotovani");
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
                    case "Vydej":
                        _evnt = new CislovaneNaradiVydanoDoVyrobyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe };
                        Pracoviste("12345330");
                        break;
                    case "Vraceni":
                        _evnt = new CislovaneNaradiPrijatoZVyrobyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Pracoviste("12345220");
                        break;
                    case "Odeslani":
                        _evnt = new CislovaneNaradiPredanoKOpraveEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava" };
                        Objednavka("D005", "123/2014");
                        TerminDodani(_parent._test.CurrentTime.Date.AddDays(15));
                        break;
                    case "Opraveni":
                        _evnt = new CislovaneNaradiPrijatoZOpravyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave, UpresneniZakladu = "Oprava" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Objednavka("D005", "123/2014");
                        DodaciList("D123/2014");
                        Opravene();
                        break;
                    case "Srotovani":
                        _evnt = new CislovaneNaradiPredanoKeSesrotovaniEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSrotu };
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

                var odeslani = _evnt as CislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.KodDodavatele = dodavatel;
                }

                var opraveni = _evnt as CislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.KodDodavatele = dodavatel;
                }

                return this;
            }

            public PresunCislovanehoBuilder Pracoviste(string pracoviste)
            {
                var vydej = _evnt as CislovaneNaradiVydanoDoVyrobyEvent;
                if (vydej != null)
                {
                    vydej.KodPracoviste = pracoviste;
                    vydej.NoveUmisteni.Pracoviste = pracoviste;
                }

                var vraceni = _evnt as CislovaneNaradiPrijatoZVyrobyEvent;
                if (vraceni != null)
                {
                    vraceni.KodPracoviste = pracoviste;
                    vraceni.PredchoziUmisteni.Pracoviste = pracoviste;
                }

                return this;
            }

            public PresunCislovanehoBuilder VPoradku(string vada)
            {
                return VeStavu(StavNaradi.VPoradku, vada);
            }

            public PresunCislovanehoBuilder Poskozene(string vada)
            {
                return VeStavu(StavNaradi.NutnoOpravit, vada);
            }

            public PresunCislovanehoBuilder Neopravitelne(string vada)
            {
                return VeStavu(StavNaradi.Neopravitelne, vada);
            }

            public PresunCislovanehoBuilder VPoradku()
            {
                return VeStavuOpravy(StavNaradi.VPoradku, StavNaradiPoOprave.OpravaNepotrebna);
            }

            public PresunCislovanehoBuilder Opravene()
            {
                return VeStavuOpravy(StavNaradi.VPoradku, StavNaradiPoOprave.Opraveno);
            }

            public PresunCislovanehoBuilder Neopravitelne()
            {
                return VeStavuOpravy(StavNaradi.Neopravitelne, StavNaradiPoOprave.Neopravitelne);
            }

            private PresunCislovanehoBuilder VeStavu(StavNaradi stavNaradi, string vada)
            {
                var vraceni = _evnt as CislovaneNaradiPrijatoZVyrobyEvent;
                if (vraceni != null)
                {
                    vraceni.StavNaradi = stavNaradi;
                    vraceni.KodVady = vada;
                    vraceni.NoveUmisteni.UpresneniZakladu = stavNaradi.ToString();
                }

                return this;
            }

            private PresunCislovanehoBuilder VeStavuOpravy(StavNaradi stavNaradi, StavNaradiPoOprave poOprave)
            {
                var opraveni = _evnt as CislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.StavNaradi = stavNaradi;
                    opraveni.NoveUmisteni.UpresneniZakladu = stavNaradi.ToString();
                    opraveni.Opraveno = poOprave;
                }

                return this;
            }

            public PresunCislovanehoBuilder Objednavka(string dodavatel, string objednavka)
            {
                var odeslani = _evnt as CislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.KodDodavatele = dodavatel;
                    odeslani.Objednavka = objednavka;
                    odeslani.NoveUmisteni.Dodavatel = dodavatel;
                    odeslani.NoveUmisteni.Objednavka = objednavka;
                }

                var opraveni = _evnt as CislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.KodDodavatele = dodavatel;
                    opraveni.Objednavka = objednavka;
                    opraveni.PredchoziUmisteni.Dodavatel = dodavatel;
                    opraveni.PredchoziUmisteni.Objednavka = objednavka;
                }

                return this;
            }

            public PresunCislovanehoBuilder DodaciList(string dodaciList)
            {
                var opraveni = _evnt as CislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.DodaciList = dodaciList;
                }

                return this;
            }

            public PresunCislovanehoBuilder TerminDodani(DateTime termin)
            {
                var odeslani = _evnt as CislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.TerminDodani = termin;
                }

                return this;
            }

            public PresunCislovanehoBuilder Oprava()
            {
                return PouzitTypOpravy(TypOpravy.Oprava);
            }

            public PresunCislovanehoBuilder Reklamace()
            {
                return PouzitTypOpravy(TypOpravy.Reklamace);
            }

            private PresunCislovanehoBuilder PouzitTypOpravy(TypOpravy typOpravy)
            {
                var odeslani = _evnt as CislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.TypOpravy = typOpravy;
                    odeslani.NoveUmisteni.UpresneniZakladu = typOpravy.ToString();
                }

                var opraveni = _evnt as CislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.TypOpravy = typOpravy;
                    opraveni.PredchoziUmisteni.UpresneniZakladu = typOpravy.ToString();
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
                    case "Vydej":
                        _parent._test.SendEvent((CislovaneNaradiVydanoDoVyrobyEvent)_evnt);
                        break;
                    case "Vraceni":
                        _parent._test.SendEvent((CislovaneNaradiPrijatoZVyrobyEvent)_evnt);
                        break;
                    case "Odeslani":
                        _parent._test.SendEvent((CislovaneNaradiPredanoKOpraveEvent)_evnt);
                        break;
                    case "Opraveni":
                        _parent._test.SendEvent((CislovaneNaradiPrijatoZOpravyEvent)_evnt);
                        break;
                    case "Srotovani":
                        _parent._test.SendEvent((CislovaneNaradiPredanoKeSesrotovaniEvent)_evnt);
                        break;
                    default:
                        throw new NotSupportedException("Nepodporovany typ pro odesilani");
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
                    case "Vydej":
                        _evnt = new NecislovaneNaradiVydanoDoVyrobyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe };
                        Pracoviste("12345220");
                        break;
                    case "Vraceni":
                        _evnt = new NecislovaneNaradiPrijatoZVyrobyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeVyrobe };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Pracoviste("12345220");
                        break;
                    case "Odeslani":
                        _evnt = new NecislovaneNaradiPredanoKOpraveEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "NutnoOpravit" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave };
                        Objednavka("D005", "123/2014");
                        TerminDodani(_parent._test.CurrentTime.Date.AddDays(15));
                        PouzitTypOpravy(TypOpravy.Oprava);
                        break;
                    case "Opraveni":
                        _evnt = new NecislovaneNaradiPrijatoZOpravyEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VOprave };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "VPoradku" };
                        Objednavka("D005", "123/2014");
                        DodaciList("D123/2014");
                        PouzitTypOpravy(TypOpravy.Oprava);
                        Opravene();
                        break;
                    case "Srotovani":
                        _evnt = new NecislovaneNaradiPredanoKeSesrotovaniEvent();
                        _evnt.PredchoziUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.NaVydejne, UpresneniZakladu = "Neopravitelne" };
                        _evnt.NoveUmisteni = new UmisteniNaradiDto { ZakladniUmisteni = ZakladUmisteni.VeSrotu };
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

                var odeslani = _evnt as NecislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.KodDodavatele = dodavatel;
                }

                var opraveni = _evnt as NecislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.KodDodavatele = dodavatel;
                }

                return this;
            }

            public PresunNecislovanehoBuilder Pracoviste(string pracoviste)
            {
                var vydej = _evnt as NecislovaneNaradiVydanoDoVyrobyEvent;
                if (vydej != null)
                {
                    vydej.KodPracoviste = pracoviste;
                    vydej.NoveUmisteni.Pracoviste = pracoviste;
                }

                var vraceni = _evnt as NecislovaneNaradiPrijatoZVyrobyEvent;
                if (vraceni != null)
                {
                    vraceni.KodPracoviste = pracoviste;
                    vraceni.PredchoziUmisteni.Pracoviste = pracoviste;
                }

                return this;
            }

            public PresunNecislovanehoBuilder VPoradku()
            {
                return VeStavu(StavNaradi.VPoradku, StavNaradiPoOprave.OpravaNepotrebna);
            }

            public PresunNecislovanehoBuilder Opravene()
            {
                return VeStavu(StavNaradi.VPoradku, StavNaradiPoOprave.Opraveno);
            }

            public PresunNecislovanehoBuilder Poskozene()
            {
                return VeStavu(StavNaradi.NutnoOpravit, StavNaradiPoOprave.Neurcen);
            }

            public PresunNecislovanehoBuilder Neopravitelne()
            {
                return VeStavu(StavNaradi.Neopravitelne, StavNaradiPoOprave.Neopravitelne);
            }

            private PresunNecislovanehoBuilder VeStavu(StavNaradi stavNaradi, StavNaradiPoOprave poOprave)
            {
                var vraceni = _evnt as NecislovaneNaradiPrijatoZVyrobyEvent;
                if (vraceni != null)
                {
                    vraceni.StavNaradi = stavNaradi;
                    vraceni.NoveUmisteni.UpresneniZakladu = stavNaradi.ToString();
                }

                var opraveni = _evnt as NecislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.StavNaradi = stavNaradi;
                    opraveni.NoveUmisteni.UpresneniZakladu = stavNaradi.ToString();
                    opraveni.Opraveno = poOprave;
                }

                return this;
            }

            public PresunNecislovanehoBuilder Objednavka(string dodavatel, string objednavka)
            {
                var odeslani = _evnt as NecislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.KodDodavatele = dodavatel;
                    odeslani.Objednavka = objednavka;
                    odeslani.NoveUmisteni.Dodavatel = dodavatel;
                    odeslani.NoveUmisteni.Objednavka = objednavka;
                }

                var opraveni = _evnt as NecislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.KodDodavatele = dodavatel;
                    opraveni.Objednavka = objednavka;
                    opraveni.PredchoziUmisteni.Dodavatel = dodavatel;
                    opraveni.PredchoziUmisteni.Objednavka = objednavka;
                }

                return this;
            }

            public PresunNecislovanehoBuilder DodaciList(string dodaciList)
            {
                var opraveni = _evnt as NecislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.DodaciList = dodaciList;
                }

                return this;
            }

            public PresunNecislovanehoBuilder TerminDodani(DateTime termin)
            {
                var odeslani = _evnt as NecislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.TerminDodani = termin;
                }

                return this;
            }

            public PresunNecislovanehoBuilder Oprava()
            {
                return PouzitTypOpravy(TypOpravy.Oprava);
            }

            public PresunNecislovanehoBuilder Reklamace()
            {
                return PouzitTypOpravy(TypOpravy.Reklamace);
            }

            private PresunNecislovanehoBuilder PouzitTypOpravy(TypOpravy typOpravy)
            {
                var odeslani = _evnt as NecislovaneNaradiPredanoKOpraveEvent;
                if (odeslani != null)
                {
                    odeslani.TypOpravy = typOpravy;
                    odeslani.NoveUmisteni.UpresneniZakladu = typOpravy.ToString();
                }

                var opraveni = _evnt as NecislovaneNaradiPrijatoZOpravyEvent;
                if (opraveni != null)
                {
                    opraveni.TypOpravy = typOpravy;
                    opraveni.PredchoziUmisteni.UpresneniZakladu = typOpravy.ToString();
                }

                return this;
            }

            public Guid Send()
            {
                DoplnitCelkovyPocet();
                switch (_rezim)
                {
                    case "Prijem":
                        Send((NecislovaneNaradiPrijatoNaVydejnuEvent)_evnt);
                        break;
                    case "Vydej":
                        Send((NecislovaneNaradiVydanoDoVyrobyEvent)_evnt);
                        break;
                    case "Vraceni":
                        Send((NecislovaneNaradiPrijatoZVyrobyEvent)_evnt);
                        break;
                    case "Odeslani":
                        Send((NecislovaneNaradiPredanoKOpraveEvent)_evnt);
                        break;
                    case "Opraveni":
                        Send((NecislovaneNaradiPrijatoZOpravyEvent)_evnt);
                        break;
                    case "Srotovani":
                        Send((NecislovaneNaradiPredanoKeSesrotovaniEvent)_evnt);
                        break;
                    default:
                        throw new NotSupportedException("Rezim " + _rezim + " nepodporovan pro odesilani");
                }
                return _evnt.EventId;
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

            private void Send<T>(T evnt)
            {
                _parent._test.SendEvent(evnt);
            }
        }
    }
}
