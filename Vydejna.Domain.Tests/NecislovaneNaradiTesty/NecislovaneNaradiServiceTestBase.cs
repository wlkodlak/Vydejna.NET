using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;
using Vydejna.Domain.Tests.NaradiObecneTesty;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    public class NecislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<NecislovaneNaradi, NecislovaneNaradiService>
    {
        protected Guid _naradiId;
        protected List<object> _given;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _naradiId = new Guid("5844abcd-0000-0000-0000-111122223333");
            _given = new List<object>();
        }

        protected override NecislovaneNaradiService CreateService()
        {
            return new NecislovaneNaradiService(_repository, _time);
        }

        protected override void Execute<T>(T cmd)
        {
            Given(_naradiId, _given.ToArray());
            base.Execute<T>(cmd);
        }

        protected SkupinaNecislovanehoNaradiDto Kus(DateTime datum, decimal cena, char cerstvost, int pocet)
        {
            return new SkupinaNecislovanehoNaradiDto
            {
                Datum = datum,
                Cena = cena,
                Cerstvost = CerstvostChar(cerstvost),
                Pocet = pocet
            };
        }

        protected void OcekavaneKusy<T>(Func<T, IList<SkupinaNecislovanehoNaradiDto>> extraktor, params SkupinaNecislovanehoNaradiDto[] ocekavano)
        {
            var realne = extraktor(NewEventOfType<T>());
            Assert.IsNotNull(realne, "Pouzite kusy");
            var realneStringy = string.Join("\r\n", realne.Select(StringOcekavanychKusu).OrderBy(s => s));
            var ocekavaneStringy = string.Join("\r\n", ocekavano.Select(StringOcekavanychKusu).OrderBy(s => s));
            Assert.AreEqual(ocekavaneStringy, realneStringy);
        }

        private string CerstvostChar(char cerstvost)
        {
            switch (cerstvost)
            {
                case 'N': return "Nove";
                case 'O': return "Opravene";
                default: return "Pouzite";
            }
        }

        private char CerstvostChar(string cerstvost)
        {
            switch (cerstvost)
            {
                case "Nove": return 'N';
                case "Opravene": return 'O';
                default: return 'P';
            }
        }

        private string StringOcekavanychKusu(SkupinaNecislovanehoNaradiDto dto)
        {
            return string.Format("{0:yyyyMMdd}{1}{2}x{3:0.00}",
                dto.Datum, CerstvostChar(dto.Cerstvost), dto.Pocet, dto.Cena);
        }

        protected DateTime Datum(int den)
        {
            return GetUtcTime().Date.AddDays(-90 + den);
        }

        protected void Prijate(int pocet, decimal cena = 10m, int datum = 0)
        {
            _given.Add(new NecislovaneNaradiPrijatoNaVydejnuEvent
            {
                EventId = Guid.NewGuid(),
                Datum = Datum(datum),
                Pocet = pocet,
                CenaNova = cena,
                NaradiId = _naradiId,
                KodDodavatele = "D88",
                CelkovaCenaNova = pocet * cena,
                PrijemZeSkladu = false,
                NoveUmisteni = UmisteniNaradi.NaVydejne(StavNaradi.VPoradku).Dto(),
                NoveKusy = new List<SkupinaNecislovanehoNaradiDto> { Kus(Datum(datum), cena, 'N', pocet) }
            });
        }
    }
}
