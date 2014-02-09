using ServiceLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NecislovaneNaradi : EventSourcedGuidAggregate
    {
        public NecislovaneNaradi()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public void Execute(NecislovaneNaradiPrijmoutNaVydejnuCommand cmd, ITime time)
        {
            var evnt = new NecislovaneNaradiPrijatoNaVydejnuEvent
            {
                NaradiId = cmd.NaradiId,
                CenaNova = cmd.CenaNova,
                KodDodavatele = cmd.KodDodavatele,
                Pocet = cmd.Pocet,
                PrijemZeSkladu = cmd.PrijemZeSkladu,
                EventId = Guid.NewGuid(),
                Datum = time.GetUtcTime(),
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                CelkovaCenaNova = cmd.Pocet * cmd.CenaNova,
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto>()
            };
            evnt.NoveKusy.Add(new SkupinaNecislovanehoNaradi(evnt.Datum, evnt.CenaNova, CerstvostNecislovanehoNaradi.Nove, evnt.Pocet).Dto());
            ApplyChange(evnt);

            if (cmd.PrijemZeSkladu)
            {
                ApplyChange(new NastalaPotrebaUpravitStavNaSkladeEvent
                {
                    NaradiId = cmd.NaradiId,
                    TypZmeny = TypZmenyNaSklade.SnizitStav,
                    Hodnota = cmd.Pocet
                });
            }
        }

        private void ApplyChange(NecislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            RecordChange(evnt);
        }

        private void ApplyChange(NastalaPotrebaUpravitStavNaSkladeEvent evnt)
        {
            RecordChange(evnt);
        }

        public void Execute(NecislovaneNaradiVydatDoVyrobyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPrijmoutZVyrobyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPrijmoutZOpravyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPredatKeSesrotovaniCommand cmd, ITime time)
        {
        }
    }
}
