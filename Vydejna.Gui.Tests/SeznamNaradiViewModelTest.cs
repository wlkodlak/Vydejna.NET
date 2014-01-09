using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Gui.Common;
using Vydejna.Gui.SeznamNaradi;
using Vydejna.Contracts;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Vydejna.Gui.Shell;

namespace Vydejna.Gui.Tests
{
    [TestClass]
    public class SeznamNaradiViewModelTest
    {
        private MockRepository _repo;
        private Mock<IReadSeznamNaradi> _readSvc;
        private Mock<IWriteSeznamNaradi> _writeSvc;
        private Mock<IShell> _shell;
        private MockFunc<DefinovatNaradiViewModel> _createVM;

        private class MockFunc<R>
        {
            private R _result;
            private bool _wasCalled = false;

            public void Setup(R result)
            {
                _result = result;
            }

            public Func<R> Delegate { get { return Handler; } }
            public bool WasCalled { get { return _wasCalled; } }
            
            private R Handler()
            {
                _wasCalled = true;
                return _result;
            }
            
            public void Verify()
            {
                if (!_wasCalled)
                    throw new Exception("Delegate was not called");
            }
        }

        [TestInitialize]
        public void Setup()
        {
            _repo = new MockRepository(MockBehavior.Strict);
            _readSvc = _repo.Create<IReadSeznamNaradi>();
            _writeSvc = _repo.Create<IWriteSeznamNaradi>();
            _shell = _repo.Create<IShell>();
            _createVM = new MockFunc<DefinovatNaradiViewModel>();
        }

        [TestMethod]
        public void PriInicializaciDialoguSeNacteSeznamNaradi()
        {
            var taskResult = new TaskCompletionSource<ZiskatSeznamNaradiResponse>();
            taskResult.SetResult(PrazdnySeznamNaradi());
            var request = new ZiskatSeznamNaradiRequest(0, int.MaxValue);
            _readSvc.Setup(r => r.Handle(request)).Returns(taskResult.Task);
            var vm = VytvoritViewModel();
            vm.Handle(new UiMessages.SeznamNaradiOtevren());
            _repo.VerifyAll();
        }

        [TestMethod]
        public void KliknutiNaDefinovatNaradiOtevreNovyModulProDefinici()
        {
            _shell
                .Setup(s => s.RunModule(It.IsAny<DefinovatNaradiViewModel>()))
                .Callback<DefinovatNaradiViewModel>(d => Assert.IsNotNull(d, "IShell.RunModule(null)"));
            _createVM.Setup(new DefinovatNaradiViewModel(null, null, null));
            var vm = VytvoritViewModel();
            Assert.IsNotNull(vm.DefinovatNaradiCommand, "Chybí příkaz Definovat nářadí");
            vm.DefinovatNaradiCommand.Execute(null);
            _repo.VerifyAll();
            _createVM.Verify();
        }

        [TestMethod]
        public void PriDokonceniDefiniceNaradiSeAktualizujeSeznam()
        {
            var taskResult = new TaskCompletionSource<ZiskatSeznamNaradiResponse>();
            taskResult.SetResult(PrazdnySeznamNaradi());
            _readSvc.Setup(r => r.Handle(new ZiskatSeznamNaradiRequest(0, int.MaxValue))).Returns(taskResult.Task);
            var vm = VytvoritViewModel();
            vm.Handle(new UiMessages.DokoncenaDefiniceNaradi());
            _repo.VerifyAll();
        }

        [TestMethod]
        public void PriPrijmuVysledkuNacitaniSeTytoZobrazi()
        {
            var taskResult = new TaskCompletionSource<ZiskatSeznamNaradiResponse>();
            _readSvc.Setup(r => r.Handle(new ZiskatSeznamNaradiRequest(0, int.MaxValue))).Returns(taskResult.Task);
            var vm = VytvoritViewModel();
            var list = vm.SeznamNaradi;
            vm.Handle(new UiMessages.SeznamNaradiOtevren());
            taskResult.SetResult(NaplnenySeznamNaradi());
            Assert.AreEqual(5, list.Count);
        }

        [TestMethod]
        public void PriZmeneFiltruSeZobraziJenOdpovidajiciZaznamy()
        {
            var vm = VytvoritViewModel();
            vm.Handle(new UiMessages.NactenSeznamNaradi(NaplnenySeznamNaradi()));
            vm.HledanyText = "5";
            var filtrovano = vm.SeznamNaradi.OfType<TypNaradiViewModel>().Select(n => n.Vykres).ToList();
            var ocekavano = new string[] { "552-124a", "581-42-335" };
            CollectionAssert.AreEqual(ocekavano, filtrovano);
        }

        [TestMethod]
        public void PriNacteniZafiltrovanehoSeznamuSeFiltrAplikuje()
        {
            var vm = VytvoritViewModel();
            vm.HledanyText = "5";
            vm.Handle(new UiMessages.NactenSeznamNaradi(NaplnenySeznamNaradi()));
            var filtrovano = vm.SeznamNaradi.OfType<TypNaradiViewModel>().Select(n => n.Vykres).ToList();
            var ocekavano = new string[] { "552-124a", "581-42-335" };
            CollectionAssert.AreEqual(ocekavano, filtrovano);
        }

        [TestMethod]
        public void PriVraceniFiltruNaPrazdnyRetezecZobrazujeVsechnaNaradi()
        {
            var vm = VytvoritViewModel();
            vm.HledanyText = "532";
            vm.Handle(new UiMessages.NactenSeznamNaradi(NaplnenySeznamNaradi()));
            vm.HledanyText = "";
            Assert.AreEqual(5, vm.SeznamNaradi.Count);
        }

        [TestMethod]
        public void PriAktivaciNaradiNeaktivnihoSePoslePrikazAUpraviZobrazeni()
        {
            AktivovatNaradiCommand cmd = null;
            _writeSvc.Setup(s => s.Handle(It.IsAny<AktivovatNaradiCommand>())).Returns<AktivovatNaradiCommand>(c => { cmd = c; return null; });
            var vm = VytvoritViewModel();
            var seznamNaradi = NaplnenySeznamNaradi();
            vm.Handle(new UiMessages.NactenSeznamNaradi(seznamNaradi));
            var element = vm.SeznamNaradi.SourceCollection.Cast<TypNaradiViewModel>().ElementAt(3);
            var elementChanges = new PropertyChangedWatcher(element);
            Assert.IsInstanceOfType(vm.AktivovatNaradiCommand, typeof(ICommand), "Chybi prikaz Aktivovat");
            vm.AktivovatNaradiCommand.Execute(element);
            Assert.IsNotNull(cmd, "Prikaz neodeslan");
            Assert.AreEqual(seznamNaradi.SeznamNaradi[3].Id, cmd.NaradiId, "ID naradi");
            Assert.IsTrue(element.Aktivni, "Aktivace naradi");
            elementChanges.AssertChange("Aktivni");
        }

        [TestMethod]
        public void PriDeaktivaciNaradiAktivnihoSePoslePrikazAUpraviZobrazeni()
        {
            DeaktivovatNaradiCommand cmd = null;
            _writeSvc.Setup(s => s.Handle(It.IsAny<DeaktivovatNaradiCommand>())).Returns<DeaktivovatNaradiCommand>(c => { cmd = c; return null; });
            var vm = VytvoritViewModel();
            var seznamNaradi = NaplnenySeznamNaradi();
            vm.Handle(new UiMessages.NactenSeznamNaradi(seznamNaradi));
            var element = vm.SeznamNaradi.SourceCollection.Cast<TypNaradiViewModel>().ElementAt(1);
            var elementChanges = new PropertyChangedWatcher(element);
            Assert.IsInstanceOfType(vm.DeaktivovatNaradiCommand, typeof(ICommand), "Chybi prikaz Deaktivovat");
            vm.DeaktivovatNaradiCommand.Execute(element);
            Assert.IsNotNull(cmd, "Prikaz neodeslan");
            Assert.AreEqual(seznamNaradi.SeznamNaradi[1].Id, cmd.NaradiId, "ID naradi");
            Assert.IsFalse(element.Aktivni, "Aktivace naradi");
            elementChanges.AssertChange("Aktivni");
        }

        [TestMethod]
        public void PokusOAktivaciAktivnihoNaradiNedelaNic()
        {
            var vm = VytvoritViewModel();
            var seznamNaradi = NaplnenySeznamNaradi();
            vm.Handle(new UiMessages.NactenSeznamNaradi(seznamNaradi));
            var element = vm.SeznamNaradi.SourceCollection.Cast<TypNaradiViewModel>().ElementAt(1);
            Assert.IsInstanceOfType(vm.AktivovatNaradiCommand, typeof(ICommand), "Chybi prikaz Aktivovat");
            vm.AktivovatNaradiCommand.Execute(element);
            Assert.IsTrue(element.Aktivni, "Aktivace naradi");
        }

        private SeznamNaradiViewModel VytvoritViewModel()
        {
            return new SeznamNaradiViewModel(_shell.Object, _readSvc.Object, _writeSvc.Object, _createVM.Delegate);
        }

        private ZiskatSeznamNaradiResponse PrazdnySeznamNaradi()
        {
            return new ZiskatSeznamNaradiResponse();
        }

        private ZiskatSeznamNaradiResponse NaplnenySeznamNaradi()
        {
            var dto = new ZiskatSeznamNaradiResponse();
            dto.PocetCelkem = 5;
            dto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "1274-55871-b", "50x20x5", "", true));
            dto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "2474-6545", "o 47mm", "Brusny kotouc", true));
            dto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "552-124a", "15441", "Kladka", true));
            dto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "581-42-335", "25x10x2", "", false));
            dto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "777-88888", "o 7mm", "Sroubovak", true));
            return dto;
        }
    }

}
