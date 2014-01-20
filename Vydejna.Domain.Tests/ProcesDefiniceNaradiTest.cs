using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vydejna.Contracts;
using Vydejna.Domain;
using ServiceLib;
using ServiceLib.Tests.TestUtils;

namespace Vydejna.Domain.Tests
{
    [TestClass]
    public class ProcesDefiniceNaradiTest
    {
        private MockIHandle<DefinovatNaradiCommand> _mockDefinice;
        private MockIHandle<AktivovatNaradiCommand> _mockAktivace;
        private MockIHandle<DokoncitDefiniciNaradiInternalCommand> _mockDokonceni;
        private ProcesDefiniceNaradi _proces;

        [TestInitialize]
        public void Inicialize()
        {
            _mockDefinice = new MockIHandle<DefinovatNaradiCommand>();
            _mockAktivace = new MockIHandle<AktivovatNaradiCommand>();
            _mockDokonceni = new MockIHandle<DokoncitDefiniciNaradiInternalCommand>();
        }

        private class MockIHandle<T> : IHandle<CommandExecution<T>>
        {
            private Action<T> _action = null;

            public void SetHandler(Action<T> action)
            {
                _action = action;
            }

            public void Handle(CommandExecution<T> execution)
            {
                if (_action == null)
                    throw new AssertFailedException(
                        string.Format("Unexpected call to IHandle<{0}>", typeof(T).Name));
                else
                {
                    _action(execution.Command);
                    execution.OnCompleted();
                }
            }
        }

        private void VytvoritProces()
        {
            _proces = new ProcesDefiniceNaradi(_mockDefinice, _mockAktivace, _mockDokonceni);
        }

        private void Vykonat<T>(T evnt)
        {
            bool completed = false;
            var handler = _proces as IHandle<CommandExecution<T>>;
            handler.Handle(new CommandExecution<T>(evnt, () => completed = true, ex => { throw ex.PreserveStackTrace(); }));
            Assert.IsTrue(completed, "Handler dokoncen");
        }

        [TestMethod]
        public void ZahajeniDefinice()
        {
            var naradiId = Guid.NewGuid();
            DefinovatNaradiCommand cmd = null;
            _mockDefinice.SetHandler(c => cmd = c);
            VytvoritProces();
            Vykonat(new ZahajenaDefiniceNaradiEvent { NaradiId = naradiId, Vykres = "884-55558", Rozmer = "50x5x3", Druh = "" });
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
            VytvoritProces();
            Vykonat(new ZahajenaAktivaceNaradiEvent { NaradiId = naradiId });
            Assert.IsNotNull(cmd, "Ocekavan AktivovatNaradiCommand");
            Assert.AreEqual(naradiId, cmd.NaradiId, "NaradiId");
        }

        [TestMethod]
        public void DokonceniDefinice()
        {
            var naradiId = Guid.NewGuid();
            DokoncitDefiniciNaradiInternalCommand cmd = null;
            _mockDokonceni.SetHandler(c => cmd = c);
            VytvoritProces();
            Vykonat(new DefinovanoNaradiEvent { NaradiId = naradiId, Vykres = "884-55558", Rozmer = "50x5x3", Druh = "" });
            Assert.IsNotNull(cmd, "Ocekavan DokoncitDefiniciNaradiInternalCommand");
            Assert.AreEqual(naradiId, cmd.NaradiId, "NaradiId");
            Assert.AreEqual("884-55558", cmd.Vykres, "Vykres");
            Assert.AreEqual("50x5x3", cmd.Rozmer, "Rozmer");
            Assert.AreEqual("", cmd.Druh, "Druh");
        }
    }
}
