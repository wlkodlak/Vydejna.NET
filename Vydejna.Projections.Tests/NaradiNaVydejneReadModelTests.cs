using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class NaradiNaVydejneReadModelTests
    {
        /*
         * Neexistujici projekce dava prazdny seznam, nulove pocty, stranku z requestu
         * Nove naradi ma informace podle udalosti definice
         * Stejne naradi s jinym stavem dostava samostatny radek
         * Stejne naradi se stejnym stavem upravuje jen pocty u existujiciho radku
         * Seznam je serazen podle vykresu, rozmeru a stavu naradi
         * Pri velkem mnozstvi ruznych naradi na vydejne je serazeny seznam strankovan
         */
    }
}
