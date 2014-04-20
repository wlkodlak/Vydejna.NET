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
        
        public NaradiTestEventBuilder(ReadModelTestBase test, string naradi)
        {
            _test = test;
            _naradiId = BuildNaradiId(naradi);
        }

        protected static Guid BuildNaradiId(string naradi)
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

        protected void Definovano(string vykres, string rozmer, string druh)
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

        protected void Deaktivovano()
        {
            _test.SendEvent(new DeaktivovanoNaradiEvent
            {
                NaradiId = _naradiId,
                Verze = ++_verzeDefinovaneho
            });
        }

        protected void Aktivovano()
        {
            _test.SendEvent(new AktivovanoNaradiEvent
            {
                NaradiId = _naradiId,
                Verze = ++_verzeDefinovaneho
            });
        }

        protected void ZmenaNaSklade(int zmena, int novyStav, ZdrojZmenyNaSklade zdrojZmeny = ZdrojZmenyNaSklade.Manualne)
        {
            _test.SendEvent(new ZmenenStavNaSkladeEvent
            {
                NaradiId = _naradiId,
                DatumZmeny = _test.CurrentTime,
                ZdrojZmeny = zdrojZmeny,
                TypZmeny = zmena > 0 ? TypZmenyNaSklade.ZvysitStav : TypZmenyNaSklade.SnizitStav,
                NovyStav = novyStav,
                Hodnota = zmena > 0 ? zmena : -zmena,
            });
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
    }
}
