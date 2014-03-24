using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class NaradiNaObjednavceReadModelTests
    {
        /*
         * Neexistujici objednavka
         * - objednavka neexistuje
         * - informace o dodavateli obsahuji kod z requestu
         * - ostatni pole informaci o dodavateli jsou prazdna
         * - cislo objednavky z requestu
         * - termin dodani nullovy
         * - nulovy celkovy pocet
         * - prazdny seznam naradi
         * Vydej noveho naradi na objednavku
         * - informace o naradi z definice naradi
         * - pocet celkem, necislovanych a seznam cislovanych podle udalosti
         * - seznam muze obsahovat vice typu naradi
         * Vydej stejneho naradi na objednavku
         * - upravuji se pocty a seznamy cislovanych kusu
         * Prijem vsech kusu naradi
         * - naradi zmizi ze seznamu
         */
    }
}
