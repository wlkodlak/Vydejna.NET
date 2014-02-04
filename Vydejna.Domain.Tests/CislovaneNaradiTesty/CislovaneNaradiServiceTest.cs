namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
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
         *   - naradi musi byt dostupne pro prijem z pracoviste
         *     - spravne pracoviste
         *   - naradi nesmi byt nedostupne pro prijem z pracoviste
         *     - jeste vubec nevydano
         *     - jine pracoviste
         *     
         * - sesrotovani
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro srotovani
         *     - prijate z vyroby jako neopravitelne
         *   - naradi nesmi byt nedostupne pro srotovani
         *     - prijate z opravy jako opravene
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *     
         * - vydej na opravu
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro opravu
         *     - prijate z vyroby jako nutne opravit
         *   - naradi nesmi byt nedostupne pro opravu
         *     - prijate z vyroby jako v poradku
         *   - cena nesmi byt zaporna
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - je nutne zadat objednavku
         *   - je nutne zadat dodavatele
         *   - termin dodani musi byt v budoucnosti (relativne k datu operace)
         *   
         * - prijem z opravy
         *   - cislovane naradi musi existovat
         *   - naradi musi byt dostupne pro prijem z opravy
         *     - vydane na opravu na stejnou objednavku
         *   - naradi nesmi byt nedostupne pro prijem z opravy
         *     - vydane na reklamaci na jine objednavce
         *   - cena nesmi byt zaporna
         *   - v udalosti odpodivaji polozky prikazu
         *   - do udalosti se automaticky doplni EventId, Datum a PuvodniCena
         *   - je nutne zadat objednavku
         *   - je nutne zadat dodavatele
         *   - je nutne zadat dodaci list
         */
    }
}
