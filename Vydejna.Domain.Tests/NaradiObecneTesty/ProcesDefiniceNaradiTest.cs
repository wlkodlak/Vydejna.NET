using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;
using Vydejna.Domain.Procesy;

namespace Vydejna.Domain.Tests.NaradiObecneTesty
{
    [TestClass]
    public class ProcesDefiniceNaradiTest
    {
        private MockPublisher _bus;
        private ProcesDefiniceNaradi _proces;

        [TestInitialize]
        public void Inicialize()
        {
            _bus = new MockPublisher();
        }

        private class MockPublisher : IPublisher
        {
            private Dictionary<Type, Action<object>> _handlers = new Dictionary<Type, Action<object>>();

            public void SetHandler<T>(Action<T> action)
            {
                _handlers[typeof(CommandExecution<T>)] = o =>
                {
                    var msg = (CommandExecution<T>)o;
                    action(msg.Command);
                    msg.OnCompleted();
                };
            }

            public void Publish<T>(T message)
            {
                Action<object> handler;
                if (!_handlers.TryGetValue(typeof(T), out handler))
                {
                    throw new AssertFailedException(string.Format("Unexpected call to Publish<{0}>", typeof(T).Name));
                }
                else
                {
                    handler(message);
                }
            }
        }

        private void VytvoritProces()
        {
            _proces = new ProcesDefiniceNaradi(_bus);
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
            _bus.SetHandler<DefinovatNaradiCommand>(c => cmd = c);
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
            _bus.SetHandler<AktivovatNaradiCommand>(c => cmd = c);
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
            _bus.SetHandler<DokoncitDefiniciNaradiInternalCommand>(c => cmd = c);
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
