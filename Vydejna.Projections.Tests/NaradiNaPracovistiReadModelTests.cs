using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class NaradiNaPracovistiReadModelTests
    {
        /*
         * Pokus ziskat neexistujici pracoviste vraci prazdny vysledek
         * - pracoviste neexistuje, kod podle requestu, ostatni pole informaci o pracovisti prazdna, pocet celkem nulovy, prazdny seznam naradi
         * Odpoved obsahuje informace o pracovisti podle posledni udalosti definice pracoviste
         * Vydej na pracoviste pridava naradi, ktere v seznamu jeste neni
         * - doplnene informace o naradi
         * - pocet celkem, necislovanych, seznam cislovanych, datum posledniho vydeje
         * - seznam muze obsahovat vice typu naradi
         * Vydej jiz pouziteho naradi na pracoviste
         * - upravuje pocty naradi (celkem, necislovane, seznam cislovanych)
         * - aktualizuje se datum posledniho vydeje
         * Prijem vseho naradi z pracoviste odebere naradi ze seznamu
         */
    }
}
