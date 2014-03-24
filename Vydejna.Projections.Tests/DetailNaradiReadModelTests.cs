using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Projections.Tests
{
    class DetailNaradiReadModelTests
    {
        /*
         * Neexistujici naradi
         * - naradiId podle requestu
         * - informace o naradi prazdne retezce
         * - pocty naradi nulove (ale existujici)
         * - prazdne seznamy necislovanych a cislovanych kusu
         * Definovane naradi
         * - informace o naradi podle udalosti
         * - nulove pocty
         * - prazdne seznamy kusu
         * Prijem necislovaneho naradi na vydejnu
         * - zvysuji se pocty celkove a necislovane
         * - do seznamu necislovanych pribude radek s umistenim, celkovym poctem a detailem na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem cislovaneho naradi na vydejnu
         * - zvysuji se pocty celkove a cislovane
         * - do seznamu cislovanych pribude radek s cislem naradi, umistenim a detailem na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Vydej do vyroby
         * - upravi se pocty
         * - odstrani se puvodni detail, vytvori se detail ve vyrobe podle pracoviste
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem poskozeneho
         * - upravi se pocty
         * - detail bude pro opravu podle objednavky vcetne dodavatele
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Navrat opraveneho naradi na vydejnu
         * - pocty
         * - detail na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Prijem zniceneho naradi
         * - pocty
         * - detail na vydejne
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         * Odeslani do srotu
         * - pocty
         * - cislovane naradi se odstranuje ze seznamu
         * - pri nulovem poctu na puvodnim umisteni se radek odstrani
         */
    }
}
