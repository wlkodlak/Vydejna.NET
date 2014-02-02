using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain.Tests
{
    public class CislovaneNaradiServiceTest
    {
        /*
         * - prijem na vydejnu
         *   - cena nesmi byt zaporna
         *   - cislo naradi nesmi byt obsazeno
         *   - v udalosti odpovidaji polozky prikazu
         *   - v udalosti se automaticky doplni EventId a Datum
         *   - pri prijmu ze skladu se generuje interni udalost pro zmenu stavu na sklade
         *   
         * - vydej do vyroby
         *   - cislovane naradi musi existovat
         *   - cena nesmi byt zaporna
         *   - nutne zadat kod pracovite
         *   - naradi musi byt dostupne pro vydej do vyroby
         *     - cerstve prijato na vydejnu
         *     - prijato z vyroby v poradku
         *   - naradi nesmi byt nedostupne pro vydej do vyroby
         *     - prijato z vyroby nutne k oprave
         *     - odeslano do srotu
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         * 
         * - prijem z vyroby
         *   - cislovane naradi musi existovat
         *   - cena nesmi byt zaporna
         *   - nutne zadat kod pracoviste
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - pokud stav naradi je v poradku, vada nesmi byt zadana
         *   - pokud stav naradi neni v poradku, vada musi byt urcena
         *   - pracoviste musi byt na pracovisti
         *     - spravne pracoviste
         *     - jeste vubec nevydano
         *     
         * - sesrotovani
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro srotovani
         *     - prijate z vyroby jako neopravitelne
         *   - naradi nesmi byt nedostupne pro srotovani
         *     - prijate z opravy jako opravene
         *     
         * - vydej na opravu
         *   - cislovane naradi musi existovat
         *   - 
         */
    }
}
