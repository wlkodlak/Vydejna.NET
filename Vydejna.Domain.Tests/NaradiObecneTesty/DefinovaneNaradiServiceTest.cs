using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain.DefinovaneNaradi;

namespace Vydejna.Domain.Tests.NaradiObecneTesty
{
    [TestClass]
    public class DefinovaneNaradiServiceTest
    {
        private NaradiRepositoryMock _repository;
        private List<object> _udalosti;
        private IEnumerator<object> _aktualniUdalost;
        private Dictionary<Guid, List<object>> _obsahRepository;
        private DefinovaneNaradiService _svc;
        private TestScheduler _scheduler;
        private TestTracking _tracking;

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _repository = new NaradiRepositoryMock(this);
            _udalosti = new List<object>();
            _obsahRepository = new Dictionary<Guid, List<object>>();
            _aktualniUdalost = null;
            _tracking = new TestTracking();
        }

        private void VytvoritService()
        {
            _svc = new DefinovaneNaradiService(_repository, _tracking);
        }

        private void ZpracovatPrikaz<T>(T cmd)
        {
            var task = _scheduler.Run(() => ((IProcessCommand<T>)_svc).Handle(cmd));
            if (task.Exception != null)
                throw task.Exception.InnerException.PreserveStackTrace();
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

        private void NastavitNaradi(Guid id, params object[] events)
        {
            List<object> udalosti;
            if (!_obsahRepository.TryGetValue(id, out udalosti))
                _obsahRepository[id] = udalosti = new List<object>();
            udalosti.AddRange(events);
        }

        private class NaradiRepositoryMock : IDefinovaneNaradiRepository
        {
            private DefinovaneNaradiServiceTest _parent;

            public NaradiRepositoryMock(DefinovaneNaradiServiceTest parent)
            {
                _parent = parent;
            }

            public Task<DefinovaneNaradiAggregate> Load(IAggregateId id)
            {
                List<object> udalosti;
                var guid = ((AggregateIdGuid)id).Guid;
                if (!_parent._obsahRepository.TryGetValue(guid, out udalosti))
                    return TaskUtils.FromResult<DefinovaneNaradiAggregate>(null);
                else
                    return TaskUtils.FromResult(DefinovaneNaradiAggregate.LoadFrom(udalosti));
            }

            public Task<bool> Save(DefinovaneNaradiAggregate aggregate, IEventProcessTrackSource tracker)
            {
                var udalosti = (aggregate as IEventSourcedAggregate).GetChanges();
                _parent._udalosti.AddRange(udalosti);
                return TaskUtils.FromResult(true);
            }
        }

        [TestMethod]
        public void AktivovatNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" },
                new DeaktivovanoNaradiEvent { NaradiId = idNaradi }
                );
            VytvoritService();
            ZpracovatPrikaz(new AktivovatNaradiCommand { NaradiId = idNaradi });
            var evt = OcekavanaUdalost<AktivovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            Assert.AreNotEqual(0, evt.Verze, "Verze");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void AktivovatNeexistujiciNaradi()
        {
            VytvoritService();
            ZpracovatPrikaz(new AktivovatNaradiCommand { NaradiId = Guid.NewGuid() });
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void AktivovatAktivniNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new AktivovatNaradiCommand { NaradiId = idNaradi });
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DeaktivovatNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new DeaktivovatNaradiCommand { NaradiId = idNaradi });
            var evt = OcekavanaUdalost<DeaktivovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            Assert.AreNotEqual(0, evt.Verze, "Verze");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DeaktivovatNeexistujiciNaradi()
        {
            VytvoritService();
            ZpracovatPrikaz(new DeaktivovatNaradiCommand { NaradiId = Guid.NewGuid() });
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DeaktivovatNeaktivniNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" },
                new DeaktivovanoNaradiEvent { NaradiId = idNaradi }
                );
            VytvoritService();
            ZpracovatPrikaz(new DeaktivovatNaradiCommand { NaradiId = idNaradi });
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DefinovatNaradiInternal()
        {
            var idNaradi = Guid.NewGuid();
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiInternalCommand { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" });
            var evt = OcekavanaUdalost<DefinovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            Assert.AreEqual("5555", evt.Vykres, "Vykres");
            Assert.AreEqual("0000", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            Assert.AreNotEqual(0, evt.Verze, "Verze");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DefinovatExistujiciNaradiInternal()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            VytvoritService();
            ZpracovatPrikaz(new DefinovatNaradiInternalCommand { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" });
            ZadneDalsiUdalosti();
        }
    }
}