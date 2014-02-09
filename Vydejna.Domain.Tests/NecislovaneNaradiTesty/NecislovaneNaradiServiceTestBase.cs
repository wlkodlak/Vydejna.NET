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
        protected override NecislovaneNaradiService CreateService()
        {
            return new NecislovaneNaradiService(_repository, _time);
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
    }
}
