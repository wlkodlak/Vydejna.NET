using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class SeznamNaradiServiceTest_Naradi
    {
        private NaradiRepositoryMock _repository;
        private List<object> _udalosti;
        private IEnumerator<object> _aktualniUdalost;
        private Dictionary<Guid, List<object>> _obsahRepository;

        [TestInitialize]
        public void Initialize()
        {
            _repository = new NaradiRepositoryMock(this);
            _udalosti = new List<object>();
            _obsahRepository = new Dictionary<Guid, List<object>>();
            _aktualniUdalost = null;
        }

        private SeznamNaradiService VytvoritService()
        {
            return new SeznamNaradiService(
                _repository, 
                new Mock<IUnikatnostNaradiRepository>(MockBehavior.Strict).Object);
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

        private class NaradiRepositoryMock : INaradiRepository
        {
            private SeznamNaradiServiceTest_Naradi _parent;
            
            public NaradiRepositoryMock(SeznamNaradiServiceTest_Naradi parent)
            {
                _parent = parent;
            }

            public Task<Naradi> Get(Guid id)
            {
                List<object> udalosti;
                if (!_parent._obsahRepository.TryGetValue(id, out udalosti))
                    return TaskResult.GetCompletedTask<Naradi>(null);
                return TaskResult.GetCompletedTask(Naradi.LoadFrom(udalosti));
            }

            public Task Save(Naradi naradi)
            {
                var udalosti = (naradi as IEventSourcedAggregate).GetChanges();
                _parent._udalosti.AddRange(udalosti);
                return TaskResult.GetCompletedTask();
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
            var svc = VytvoritService();
            svc.Handle(new AktivovatNaradiCommand { NaradiId = idNaradi }).GetAwaiter().GetResult();
            var evt = OcekavanaUdalost<AktivovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void AktivovatNeexistujiciNaradi()
        {
            var svc = VytvoritService();
            svc.Handle(new AktivovatNaradiCommand { NaradiId = Guid.NewGuid() }).GetAwaiter().GetResult();
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void AktivovatAktivniNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new AktivovatNaradiCommand { NaradiId = idNaradi }).GetAwaiter().GetResult();
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DeaktivovatNaradi()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new DeaktivovatNaradiCommand { NaradiId = idNaradi }).GetAwaiter().GetResult();
            var evt = OcekavanaUdalost<DeaktivovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DeaktivovatNeexistujiciNaradi()
        {
            var svc = VytvoritService();
            svc.Handle(new DeaktivovatNaradiCommand { NaradiId = Guid.NewGuid() }).GetAwaiter().GetResult();
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
            var svc = VytvoritService();
            svc.Handle(new DeaktivovatNaradiCommand { NaradiId = idNaradi }).GetAwaiter().GetResult();
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DefinovatNaradiInternal()
        {
            var idNaradi = Guid.NewGuid();
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiInternalCommand { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }).GetAwaiter().GetResult();
            var evt = OcekavanaUdalost<DefinovanoNaradiEvent>();
            Assert.AreEqual(idNaradi, evt.NaradiId, "NaradiId");
            Assert.AreEqual("5555", evt.Vykres, "Vykres");
            Assert.AreEqual("0000", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }

        [TestMethod]
        public void DefinovatExistujiciNaradiInternal()
        {
            var idNaradi = Guid.NewGuid();
            NastavitNaradi(idNaradi,
                new DefinovanoNaradiEvent { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiInternalCommand { NaradiId = idNaradi, Vykres = "5555", Rozmer = "0000", Druh = "" }).GetAwaiter().GetResult();
            ZadneDalsiUdalosti();
        }
    }
}
