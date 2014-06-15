using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain.NecislovaneNaradi
{
    public class NecislovaneNaradiValidation
    {
        public CommandError Validace(NecislovaneNaradiPrijmoutNaVydejnuCommand cmd)
        {
            if (cmd.CenaNova < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

        public CommandError Validace(NecislovaneNaradiVydatDoVyrobyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new CommandError("KodPracoviste", "REQUIRED", "Chybi cilove pracoviste");
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

        public CommandError Validace(NecislovaneNaradiPrijmoutZVyrobyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (string.IsNullOrEmpty(cmd.KodPracoviste))
                return new CommandError("KodPracoviste", "REQUIRED", "Chybi zdrojove pracoviste");
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            if (cmd.StavNaradi == StavNaradi.Neurcen)
                return new CommandError("StavNaradi", "REQUIRED", "Stav naradi neni urcen");
            return null;
        }

        public CommandError Validace(NecislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
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

        public CommandError Validace(NecislovaneNaradiPrijmoutZOpravyCommand cmd)
        {
            if (cmd.CenaNova.HasValue && cmd.CenaNova.Value < 0)
                return new CommandError("CenaNova", "RANGE", "Cena nesmi byt zaporna");
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
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

        public CommandError Validace(NecislovaneNaradiPredatKeSesrotovaniCommand cmd)
        {
            if (cmd.Pocet <= 0)
                return new CommandError("Pocet", "RANGE", "Pouzity pocet musi byt kladny");
            return null;
        }

    }
}
