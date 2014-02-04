using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests.NaradiObecneTesty
{
    [TestClass]
    public class UnikatnostNaradiServiceTest
    {
        private UnikatnostRepositoryMock _repository;
        private List<object> _udalosti;
        private IEnumerator<object> _aktualniUdalost;
        private List<object> _obsahRepository;
        private UnikatnostNaradiService _svc;

        [TestInitialize]
        public void Initialize()
        {
            _repository = new UnikatnostRepositoryMock(this);
            _udalosti = new List<object>();
            _obsahRepository = new List<object>();
            _aktualniUdalost = null;
        }

        private void VytvoritService()
        {
            _svc = new UnikatnostNaradiService(_repository);
        }

        private void ZpracovatPrikaz<T>(T cmd)
        {
            var outcome = "none";
            var execution = new CommandExecution<T>(cmd, () => outcome = "complete", ex => { throw ex.PreserveStackTrace(); });
            var svc = (IHandle<CommandExecution<T>>)_svc;
            svc.Handle(execution);
            Assert.AreEqual("complete", outcome, "Prikaz mel byt dokoncen");
        }

        private T OcekavanaUdalost<T>()
        {
            if (_aktualniUdalost == null)
                _aktualniUdalost = _udalosti.GetEnumerator();
            Assert.IsTrue(_aktualniUdalost.MoveNext(), "Očekávána {0}, nic není k dispozici", typeof(T).Name);
            Assert.IsInstanceOfType(_aktualniUdalost.Current, typeof(T), "Událost nemá správný typ");
            return (T)_aktualniUdalost.Current;
        }

        private void ZadneDalsiUdalosti()
        {
            if (_aktualniUdalost == null)
                _aktualniUdalost = _udalosti.GetEnumerator();
            if (_aktualniUdalost.MoveNext())
                Assert.Fail("Nalezena přebytečná událost {0}", _aktualniUdalost.Current.GetType());
        }

        private void NastavitUnikatnost(params object[] events)
        {
            _obsahRepository.AddRange(events);
        }

        private class UnikatnostRepositoryMock : IUnikatnostNaradiRepository
        {
            private UnikatnostNaradiServiceTest _parent;

            public UnikatnostRepositoryMock(UnikatnostNaradiServiceTest parent)
            {
                _parent = parent;
            }

            public void Load(Action<UnikatnostNaradi> onLoaded, Action<Exception> onError)
            {
                onLoaded(UnikatnostNaradi.LoadFrom(_parent._obsahRepository));
            }

            public void Save(UnikatnostNaradi unikatnost, Action onSaved, Action onConcurrency, Action<Exception> onError)
            {
                var udalosti = (unikatnost as IEventSourcedAggregate).GetChanges();
                _parent._udalosti.AddRange(udalosti);
                onSaved();
            }
        }

        [TestMethod]
        public void ZahajitDefiniciNovehoNaradi()
        {
            var naradiId = Guid.NewGuid();
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiCommand { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaDefiniceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void PriZahajeniDefiniceNovehoNaradiDoplnitChybejiciIdNaradi()
        {
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiCommand { NaradiId = Guid.Empty, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaDefiniceNaradiEvent>();
            Assert.AreNotEqual(Guid.Empty, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void AktivovatJizExistujiciNaradi()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" },
                new DokoncenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiCommand { NaradiId = Guid.NewGuid(), Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaAktivaceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void PokudDefiniceNeniDokoncenaPriZahajeniDefiniceNedelatNic()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiCommand { NaradiId = Guid.NewGuid(), Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DokoncitDefinici()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new DokoncitDefiniciNaradiInternalCommand { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<DokoncenaDefiniceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }
    }
}
