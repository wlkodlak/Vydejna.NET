using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using Vydejna.Contracts;
using ServiceLib.Tests.TestUtils;
using System.Threading;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    [TestClass]
    public class PrijemNaVydejnuTest
    {
        private TestExecutor _executor;
        private CislovaneNaradiService _svc;
        private Exception _caughtException;
        private TestRepository<CislovaneNaradi> _repository;

        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _repository = new TestRepository<CislovaneNaradi>();
            _svc = new CislovaneNaradiService(_repository);
        }

        private void Execute<T>(T cmd)
        {
            var mre = new ManualResetEventSlim();
            var msg = new CommandExecution<T>(cmd, () => mre.Set(), ex => { _caughtException = ex; mre.Set(); });
            ((IHandle<CommandExecution<T>>)_svc).Handle(msg);
            for (int i = 0; i < 3; i++)
            {
                _executor.Process();
                if (mre.Wait(10))
                    break;
            }
        }

        private void Exception<TException>()
        {
            Assert.IsNotNull(_caughtException, "Expected exception");
            if (_caughtException is TException)
                return;
            throw _caughtException.PreserveStackTrace();
        }

        private class TestRepository<T> : IEventSourcedRepository<T>
            where T : class, IEventSourcedAggregate, new()
        {
            private Dictionary<Guid, List<object>> _allEvents;
            private List<object> _newEvents;
            private bool _throwConcurrency;

            public TestRepository()
            {
                _allEvents = new Dictionary<Guid, List<object>>();
                _newEvents = new List<object>();
            }

            public void AddEvents(Guid id, params object[] events)
            {
                List<object> all;
                if (!_allEvents.TryGetValue(id, out all))
                    _allEvents[id] = all = new List<object>();
                all.AddRange(events);
            }

            public void ThrowConcurrency()
            {
                _throwConcurrency = true;
            }

            public IList<object> NewEvents()
            {
                return _newEvents;
            }

            public void Load(Guid id, Action<T> onLoaded, Action onMissing, Action<Exception> onError)
            {
                List<object> all;
                if (!_allEvents.TryGetValue(id, out all))
                    onMissing();
                else
                {
                    var aggregate = new T();
                    aggregate.LoadFromEvents(all);
                    aggregate.CommitChanges(all.Count);
                    onLoaded(aggregate);
                }
            }

            public void Save(T aggregate, Action onSaved, Action onConcurrency, Action<Exception> onError)
            {
                try
                {
                    if (_throwConcurrency)
                        onConcurrency();
                    else
                    {
                        var events = aggregate.GetChanges();
                        var id = aggregate.Id;

                        List<object> all;
                        if (!_allEvents.TryGetValue(id, out all))
                            _allEvents[id] = all = new List<object>();
                        all.AddRange(events);
                        _newEvents.AddRange(events);

                        aggregate.CommitChanges(all.Count);
                    }
                }
                catch (Exception ex)
                {
                    onError(ex); 
                    return;
                }
                onSaved();
            }
        }

        [TestMethod]
        public void CenaNesmiBytZaporna()
        {
            Execute(new CislovaneNaradiPrijmoutNaVydejnuCommand
                {
                    NaradiId = new Guid("87228724-1111-1111-1111-222233334444"),
                    CenaNova = -5,
                    CisloNaradi = 1,
                    KodDodavatele = "D58",
                    PrijemZeSkladu = false
                });
            Exception<ArgumentOutOfRangeException>();
        }


    }
    /*
     * - prijem na vydejnu
     *   - cena nesmi byt zaporna
     *   - cislo naradi nesmi byt obsazeno
     *   - v udalosti odpovidaji polozky prikazu
     *   - v udalosti se automaticky doplni EventId a Datum
     *   - pri prijmu ze skladu se generuje interni udalost pro zmenu stavu na sklade
     */
}
