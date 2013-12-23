using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class ProcesDefiniceNaradiTest
    {
        private MockIHandle<DefinovatNaradiCommand> _mockDefinice;
        private MockIHandle<AktivovatNaradiCommand> _mockAktivace;
        private MockIHandle<DokoncitDefiniciNaradiInternalCommand> _mockDokonceni;

        [TestInitialize]
        public void Inicialize()
        {
            _mockDefinice = new MockIHandle<DefinovatNaradiCommand>();
            _mockAktivace = new MockIHandle<AktivovatNaradiCommand>();
            _mockDokonceni = new MockIHandle<DokoncitDefiniciNaradiInternalCommand>();
        }

        private class MockIHandle<T> : IHandle<T>
        {
            private Action<T> _action = null;

            public void SetHandler(Action<T> action)
            {
                _action = action;
            }

            public Task Handle(T message)
            {
                if (_action == null)
                    return TaskResult.GetFailedTask<object>(new AssertFailedException(
                        string.Format("Unexpected call to IHandle<{0}>", typeof(T).Name)));
                else
                {
                    _action(message);
                    return TaskResult.GetCompletedTask();
                }
            }
        }

        private ProcesDefiniceNaradi VytvoritProces()
        {
            return new ProcesDefiniceNaradi(_mockDefinice, _mockAktivace, _mockDokonceni);
        }

        [TestMethod]
        public void ZahajeniDefinice()
        {
            var naradiId = Guid.NewGuid();
            DefinovatNaradiCommand cmd = null;
            _mockDefinice.SetHandler(c => cmd = c);
            var proces = VytvoritProces();
            proces.Handle(new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "884-55558", Rozmer = "50x5x3", Druh = "" }).GetAwaiter().GetResult();
            Assert.IsNotNull(cmd, "Ocekavan DefinovatNaradiCommand");
            Assert.AreEqual(naradiId, cmd.NaradiId, "NaradiId");
            Assert.AreEqual("884-55558", cmd.Vykres, "Vykres");
            Assert.AreEqual("50x5x3", cmd.Rozmer, "Rozmer");
            Assert.AreEqual("", cmd.Druh, "Druh");
        }

        [TestMethod]
        public void ZahajeniAktivace()
        {
            var naradiId = Guid.NewGuid();
            AktivovatNaradiCommand cmd = null;
            _mockAktivace.SetHandler(c => cmd = c);
            var proces = VytvoritProces();
            proces.Handle(new ZahajenaAktivaceNaradiEvent { NaradiId = naradiId }).GetAwaiter().GetResult();
            Assert.IsNotNull(cmd, "Ocekavan AktivovatNaradiCommand");
            Assert.AreEqual(naradiId, cmd.NaradiId, "NaradiId");
        }

        [TestMethod]
        public void DokonceniDefinice()
        {
            var naradiId = Guid.NewGuid();
            DokoncitDefiniciNaradiInternalCommand cmd = null;
            _mockDokonceni.SetHandler(c => cmd = c);
            var proces = VytvoritProces();
            proces.Handle(new DefinovanoNaradiEvent { NaradiId = naradiId, Vykres = "884-55558", Rozmer = "50x5x3", Druh = "" }).GetAwaiter().GetResult();
            Assert.IsNotNull(cmd, "Ocekavan DokoncitDefiniciNaradiInternalCommand");
            Assert.AreEqual(naradiId, cmd.NaradiId, "NaradiId");
            Assert.AreEqual("884-55558", cmd.Vykres, "Vykres");
            Assert.AreEqual("50x5x3", cmd.Rozmer, "Rozmer");
            Assert.AreEqual("", cmd.Druh, "Druh");
        }

        [TestMethod]
        public void ConsumerSetup()
        {
            var proces = VytvoritProces();
            Assert.AreEqual("ProcesDefiniceNaradi", proces.GetConsumerName());
            proces.HandleShutdown().GetAwaiter().GetResult();
        }
    }
}
