using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NecislovaneNaradiValidation
    {
        public ValidationErrorException Validace(NecislovaneNaradiPrijmoutNaVydejnuCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

        public ValidationErrorException Validace(NecislovaneNaradiVydatDoVyrobyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new ValidationErrorException("KodPracoviste", "REQUIRED", "Chybi cilove pracoviste");
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

        public ValidationErrorException Validace(NecislovaneNaradiPrijmoutZVyrobyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new ValidationErrorException("KodPracoviste", "REQUIRED", "Chybi zdrojove pracoviste");
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            if (cmd.StavNaradi == StavNaradi.Neurcen)
                return new ValidationErrorException("StavNaradi", "REQUIRED", "Stav naradi neni urcen");
            return null;
        }

        public ValidationErrorException Validace(NecislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
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

        public ValidationErrorException Validace(NecislovaneNaradiPrijmoutZOpravyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new ValidationErrorException("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
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

        public ValidationErrorException Validace(NecislovaneNaradiPredatKeSesrotovaniCommand cmd)
        {
            if (cmd.Pocet <= 0)
                return new ValidationErrorException("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

    }
}
