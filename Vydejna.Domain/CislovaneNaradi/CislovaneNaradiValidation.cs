using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain.CislovaneNaradi
{
    public class CislovaneNaradiValidation
    {
        public CommandError Validace(CislovaneNaradiPrijmoutNaVydejnuCommand cmd)
        {
            if (cmd.CisloNaradi <= 0)
                return new CommandError("CisloNaradi", "RANGE", "Cislo naradi musi byt kladne");
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            return null;
        }

        public CommandError Validace(CislovaneNaradiVydatDoVyrobyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new CommandError("KodPracoviste", "REQUIRED", "Chybi cilove pracoviste");
            return null;
        }

        public CommandError Validace(CislovaneNaradiPrijmoutZVyrobyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new CommandError("KodPracoviste", "REQUIRED", "Chybi zdrojove pracoviste");
            if (cmd.StavNaradi == StavNaradi.Neurcen)
                return new CommandError("StavNaradi", "REQUIRED", "Stav naradi neni urcen");
            else if (cmd.StavNaradi == StavNaradi.VPoradku)
            {
                if (!string.IsNullOrEmpty(cmd.KodVady))
                    return new CommandError("KodVady", "RANGE", "Naradi v poradku nesmi mit uvedenou vadu");
            }
            else
            {
                if (string.IsNullOrEmpty(cmd.KodVady))
                    return new CommandError("KodVady", "REQUIRED", "Poskozene musi mit uvedenou vadu");
            }
            return null;
        }

        public CommandError Validace(CislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                return new CommandError("Objednavka", "REQUIRED", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                return new CommandError("KodDodavatele", "REQUIRED", "Nutne zadat dodavatele");
            if (cmd.TerminDodani < time.GetUtcTime().Date)
                return new CommandError("TerminDodani", "RANGE", "Termin dodani nesmi byt v minulosti");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                return new CommandError("TypOpravy", "REQUIRED", "Nutne urcit typ opravy");
            return null;
        }

        public CommandError Validace(CislovaneNaradiPrijmoutZOpravyCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.Objednavka))
                return new CommandError("Objednavka", "REQUIRED", "Nutne zadat cislo objednavky");
            if (string.IsNullOrEmpty(cmd.DodaciList))
                return new CommandError("DodaciList", "REQUIRED", "Nutne zadat cislo dodaciho listu");
            if (string.IsNullOrEmpty(cmd.KodDodavatele))
                return new CommandError("KodDodavatele", "REQUIRED", "Nutne zadat dodavatele");
            if (cmd.TypOpravy == TypOpravy.Zadna)
                return new CommandError("TypOpravy", "REQUIRED", "Nutne urcit typ opravy");
            if (cmd.Opraveno == StavNaradiPoOprave.Neurcen)
                return new CommandError("Opraveno", "REQUIRED", "Nutne urcit vysledek opravy");
            return null;
        }

        public CommandError Validace(CislovaneNaradiPredatKeSesrotovaniCommand cmd)
        {
            return null;
        }
    }
}
