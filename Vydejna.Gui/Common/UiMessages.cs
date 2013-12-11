using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Gui.Common
{
    public static class UiMessages
    {
        public class SeznamNaradiOtevren { }
        public class DokoncenaDefiniceOtevrena { }
        public class DokoncenaDefiniceNaradi { }

        public class NactenSeznamNaradi
        {
            public ZiskatSeznamNaradiResponse SeznamNaradiDto { get; private set; }
            public NactenSeznamNaradi(ZiskatSeznamNaradiResponse dto) { this.SeznamNaradiDto = dto; }
        }

        public class ValidovanoDefinovatNaradi
        {
            public List<ChybaValidaceDefinovatNaradi> Chyby { get; private set; }
            public ValidovanoDefinovatNaradi()
            {
                Chyby = new List<ChybaValidaceDefinovatNaradi>();
            }
            public ValidovanoDefinovatNaradi Chyba(string polozka, string chyba)
            {
                Chyby.Add(new ChybaValidaceDefinovatNaradi { Polozka = polozka, Chyba = chyba });
                return this;
            }
        }

        public class ChybaValidaceDefinovatNaradi
        {
            public string Polozka;
            public string Chyba;
        }
    }
}
