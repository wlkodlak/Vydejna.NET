using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class PrehledAktivnihoNaradiReadModelTests
    {
        /*
         * Neexistujici projekce obsahuje stranku z requestu, pocty nulove, prazdny seznam
         * Nove definovane naradi se prida do seznamu
         * Presuny naradi upravuji pocty tohoto naradi
         * - prijem na vydejnu
         * - vydej na pracoviste
         * - prijem z pracoviste v poradku
         * - prijem poskozeneho naradi
         * - prijem zniceneho naradi
         * - predani k oprave
         * - prijem opraveneho naradi
         * - prijem neopravitelneho naradi z opravy
         * - odeslani do srotu
         * Seznam serazen podle vykresu a rozmeru
         * Neaktivni naradi v seznamu chybi
         * Znovu aktivovane naradi se do seznamu vraci s jeho aktualnimi (ne nutne nulovymi) pocty
         * Dlouhy seznam naradi je strankovan pri zachovani celkoveho razeni
         */
    }
}
