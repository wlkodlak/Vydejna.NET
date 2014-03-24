using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class IndexObjednavekReadModelTests
    {
        /*
         * Neexistujici cislo objednavky
         * - objednavka nenalezena
         * - cislo podle requestu
         * - prazdny seznam kandidatu
         * Existujici objednavka
         * - objednavka nalezena vcetne cisla
         * - kandidat obsahuje informace o cislu objednavky, kod a nazev dodavatele a termin dodani
         * Objednavka muze mit vice kandidatu
         * 
         * Neexistujici cislo dodaciho listu
         * - dodaci list nenalezen
         * - cislo podle requestu
         * - prazdny seznam kadidatu
         * Existujici dodaci list
         * - dodaci list nalezen vcetne cisla
         * - kandidat obsahuje informace o cislu dodaciho listu, kod a nazev dodavatele
         * - kandidat obsahuje seznam cisel objednavek, ke kterym je naradi dodano
         * Kandidatu pro dodaci list muze byt vice
         */
    }
}
