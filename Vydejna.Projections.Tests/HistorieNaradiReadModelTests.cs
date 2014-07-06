using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Projections.HistorieNaradiReadModel;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Projections.Tests
{
    public class HistorieNaradiReadModelTest_Reader : HistorieNaradiReadModelTestBase
    {
        /*
         * Vygenerovat 380 udalosti
         * Pri vyzadani stranky 2 bez filtru je celkovy pocet 380, ve strance 100 udalosti od data X do Y, pocet stranek 3.
         * Pri filtrovani podle cisla naradi jsou ve vysledku pouze operace nad timto cislem a jsou tam vsechny.
         * Dalsi filtrovaci testy nejsou potreba - reader samotne filtrovani neprovadi - deleguje ho na repository.
         */
    }

    public class HistorieNaradiReadModelTest_Projection : HistorieNaradiReadModelTestBase
    {
        /*
         * Na uvod definovat vadu, dodavatele, naradi a pracoviste (od kazdeho 1 kus)
         * Jedna udalost pro kazdy typ operace, overit spravne vygenerovani operace do DB operaci
         * Otestovat preziti pri pouziti udalosti bez nadefinovanych zavislosti (dodavatel, vada, pracoviste, naradi)
         * 
         */
    }


    public class HistorieNaradiReadModelTestBase : ReadModelTestBase
    {
        protected TestRepositoryOperaci _dbOperaci;
        protected HistorieNaradiResponse _response;
        protected ProjectionTestEventBuilder _build;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            _response = null;
            _dbOperaci = new TestRepositoryOperaci();
            _build = new ProjectionTestEventBuilder(this);
        }

        protected override IEventProjection CreateProjection()
        {
            return new HistorieNaradiProjection(_dbOperaci, new HistorieNaradiRepositoryPomocne(_folder), _time);
        }

        protected override object CreateReader()
        {
            return new HistorieNaradiReader(_dbOperaci);
        }

        protected void ZiskatHistorii(HistorieNaradiRequest request)
        {
            _response = ReadProjection<HistorieNaradiRequest, HistorieNaradiResponse>(request);
        }

        public class TestRepositoryOperaci : IHistorieNaradiRepositoryOperace
        {
            private readonly List<HistorieNaradiOperace> _data = new List<HistorieNaradiOperace>();

            public List<HistorieNaradiOperace> Data { get { return _data; } }

            public Task Reset()
            {
                _data.Clear();
                return TaskUtils.CompletedTask();
            }

            public Task UlozitNovouOperaci(HistorieNaradiOperace operace)
            {
                _data.Add(operace);
                return TaskUtils.CompletedTask();
            }

            public Task StornovatOperaci(Guid eventId)
            {
                foreach (var operace in _data)
                {
                    if (operace.EventId == eventId)
                    {
                        operace.Stornovano = true;
                    }
                }
                return TaskUtils.CompletedTask();
            }

            public Task<Tuple<int, List<HistorieNaradiOperace>>> Najit(HistorieNaradiRequest filtr)
            {
                var filtrovanyDotaz = _data.Where(o => filtr.DatumOd <= o.Datum && o.Datum <= filtr.DatumDo);
                switch (filtr.TypFiltru)
                {
                    case HistorieNaradiTypFiltru.CislovaneNaradi:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.CisloNaradi == filtr.CisloNaradi);
                        break;
                    case HistorieNaradiTypFiltru.Naradi:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.NaradiId == filtr.NaradiId);
                        break;
                    case HistorieNaradiTypFiltru.Objednavka:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.KodDodavatele == filtr.KodDodavatele && o.CisloObjednavky == filtr.CisloObjednavky);
                        break;
                    case HistorieNaradiTypFiltru.Pracoviste:
                        filtrovanyDotaz = filtrovanyDotaz.Where(o => o.KodPracoviste == filtr.KodPracoviste);
                        break;
                }
                var filtrovanySeznam = filtrovanyDotaz.OrderByDescending(o => o.Datum).ToList();
                var pocetNalezenych = filtrovanySeznam.Count;
                var vyslednySeznam = filtrovanySeznam.Skip(filtr.Stranka * 100 - 100).Take(100).ToList();
                return TaskUtils.FromResult(Tuple.Create(pocetNalezenych, vyslednySeznam));
            }
        }
    }
}
