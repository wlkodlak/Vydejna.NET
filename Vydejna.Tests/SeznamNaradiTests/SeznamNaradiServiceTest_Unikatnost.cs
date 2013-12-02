using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Contracts;
using Vydejna.Domain;
using Moq;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass, NUnit.Framework.TestFixture]
    public class SeznamNaradiServiceTest_Unikatnost
    {
        private UnikatnostRepositoryMock _repository;
        private List<object> _udalosti;
        private IEnumerator<object> _aktualniUdalost;
        private List<object> _obsahRepository;

        [TestInitialize, NUnit.Framework.SetUp]
        public void Initialize()
        {
            _repository = new UnikatnostRepositoryMock(this);
            _udalosti = new List<object>();
            _obsahRepository = new List<object>();
            _aktualniUdalost = null;
        }

        private SeznamNaradiService VytvoritService()
        {
            return new SeznamNaradiService(
                new Mock<INaradiRepository>(MockBehavior.Strict).Object,
                _repository
                );
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
            private SeznamNaradiServiceTest_Unikatnost _parent;

            public UnikatnostRepositoryMock(SeznamNaradiServiceTest_Unikatnost parent)
            {
                _parent = parent;
            }

            public UnikatnostNaradi Get()
            {
                return UnikatnostNaradi.LoadFrom(_parent._obsahRepository);
            }

            public void Save(UnikatnostNaradi unikatnost)
            {
                var udalosti = (unikatnost as IEventSourcedAggregate).GetChanges();
                _parent._udalosti.AddRange(udalosti);
            }
        }

        [TestMethod, NUnit.Framework.Test]
        public void ZahajitDefiniciNovehoNaradi()
        {
            var naradiId = Guid.NewGuid();
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiCommand { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaDefiniceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }

        [TestMethod, NUnit.Framework.Test]
        public void PriZahajeniDefiniceNovehoNaradiDoplnitChybejiciIdNaradi()
        {
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiCommand { NaradiId = Guid.Empty, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaDefiniceNaradiEvent>();
            Assert.AreNotEqual(Guid.Empty, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }

        [TestMethod, NUnit.Framework.Test]
        public void AktivovatJizExistujiciNaradi()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" },
                new DokoncenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiCommand { NaradiId = Guid.NewGuid(), Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<ZahajenaAktivaceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            ZadneDalsiUdalosti();
        }

        [TestMethod, NUnit.Framework.Test]
        public void PokudDefiniceNeniDokoncenaPriZahajeniDefiniceNedelatNic()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new DefinovatNaradiCommand { NaradiId = Guid.NewGuid(), Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            ZadneDalsiUdalosti();
        }

        [TestMethod, NUnit.Framework.Test]
        public void DokoncitDefinici()
        {
            var naradiId = Guid.NewGuid();
            NastavitUnikatnost(
                new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" }
                );
            var svc = VytvoritService();
            svc.Handle(new DokoncitDefiniciNaradiInternalCommand { NaradiId = naradiId, Vykres = "1248-5574-b", Rozmer = "o 500", Druh = "" });
            var evt = OcekavanaUdalost<DokoncenaDefiniceNaradiEvent>();
            Assert.AreEqual(naradiId, evt.NaradiId, "NaradiId");
            Assert.AreEqual("1248-5574-b", evt.Vykres, "Vykres");
            Assert.AreEqual("o 500", evt.Rozmer, "Rozmer");
            Assert.AreEqual("", evt.Druh, "Druh");
            ZadneDalsiUdalosti();
        }
    }
}
