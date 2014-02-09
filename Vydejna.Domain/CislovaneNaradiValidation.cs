using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class CislovaneNaradiValidation
    {
        public ValidationErrorException Validace(CislovaneNaradiPrijmoutNaVydejnuCommand cmd)
        {
            if (cmd.CisloNaradi <= 0)
                return new ValidationErrorException("CisloNaradi", "RANGE", "Cislo naradi musi byt kladne");
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            return null;
        }

        public ValidationErrorException Validace(CislovaneNaradiVydatDoVyrobyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new ValidationErrorException("KodPracoviste", "REQUIRED", "Chybi cilove pracoviste");
            return null;
        }

        public ValidationErrorException Validace(CislovaneNaradiPrijmoutZVyrobyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new ValidationErrorException("KodPracoviste", "REQUIRED", "Chybi zdrojove pracoviste");
            if (cmd.StavNaradi == StavNaradi.Neurcen)
                return new ValidationErrorException("StavNaradi", "REQUIRED", "Stav naradi neni urcen");
            else if (cmd.StavNaradi == StavNaradi.VPoradku)
            {
                if (!string.IsNullOrEmpty(cmd.KodVady))
                    return new ValidationErrorException("KodVady", "RANGE", "Naradi v poradku nesmi mit uvedenou vadu");
            }
            else
            {
                if (string.IsNullOrEmpty(cmd.KodVady))
                    return new ValidationErrorException("KodVady", "REQUIRED", "Poskozene musi mit uvedenou vadu");
            }
            return null;
        }

        public ValidationErrorException Validace(CislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                return new ValidationErrorException("Objednavka", "REQUIRED", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                return new ValidationErrorException("KodDodavatele", "REQUIRED", "Nutne zadat dodavatele");
            if (cmd.TerminDodani < time.GetUtcTime().Date)
                return new ValidationErrorException("TerminDodani", "RANGE", "Termin dodani nesmi byt v minulosti");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                return new ValidationErrorException("TypOpravy", "REQUIRED", "Nutne urcit typ opravy");
            return null;
        }

        public ValidationErrorException Validace(CislovaneNaradiPrijmoutZOpravyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                return new ValidationErrorException("Objednavka", "REQUIRED", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.DodaciList))
                return new ValidationErrorException("DodaciList", "REQUIRED", "Nutne zadat cislo dodaciho listu");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                return new ValidationErrorException("KodDodavatele", "REQUIRED", "Nutne zadat dodavatele");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                return new ValidationErrorException("TypOpravy", "REQUIRED", "Nutne urcit typ opravy");
            if (cmd.Opraveno == StavNaradiPoOprave.Neurcen)
                return new ValidationErrorException("Opraveno", "REQUIRED", "Nutne urcit vysledek opravy");
            return null;
        }

        public ValidationErrorException Validace(CislovaneNaradiPredatKeSesrotovaniCommand cmd)
        {
            return null;
        }
    }
}
