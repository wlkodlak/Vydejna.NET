using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Gui;
using Vydejna.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Windows.Input;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class DefinovatNaradiViewModelTest
    {
        private MockRepository _repo;
        private Mock<IWriteSeznamNaradi> _writeSvc;
        private Mock<IDefinovatNaradiValidator> _validator;
        private Mock<IEventPublisher> _bus;

        [TestInitialize]
        public void Initialize()
        {
            _repo = new MockRepository(MockBehavior.Strict);
            _writeSvc = _repo.Create<IWriteSeznamNaradi>();
            _validator = _repo.Create<IDefinovatNaradiValidator>();
            _bus = _repo.Create<IEventPublisher>();
        }

        [TestMethod]
        public void PriStartuJsouPolePrazdnaATlacitkoZakazane()
        {
            var vm = VytvoritViewModel();
            Assert.AreEqual("", vm.Vykres, "Vykres");
            Assert.AreEqual("", vm.Rozmer, "Rozmer");
            Assert.AreEqual("", vm.Druh, "Druh");
            Assert.IsTrue(vm.PovolenyUpravy, "PovolenyUpravy");
            Assert.IsInstanceOfType(vm.DefinovatNaradiCommand, typeof(ICommand), "DefinovatNaradiCommand");
            Assert.IsFalse(vm.DefinovatNaradiCommand.CanExecute(null), "DefinovatNaradiCommand.Enabled");
        }

        [TestMethod]
        public void PriStartuJsouPoleOznacenaChybou()
        {
            var vm = VytvoritViewModel();
            _validator.Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>())).Verifiable();
            var watcher = new PropertyErrorWatcher(vm, "Vykres", "Rozmer");
            vm.Handle(new UiMessages.DokoncenaDefiniceOtevrena());
            vm.Handle(
                new UiMessages.ValidovanoDefinovatNaradi()
                .Chyba("Vykres", "Chybí").Chyba("Rozmer", "Chybí"));
            _repo.Verify();
            watcher.AssertError("Vykres", PropertyErrorWatcher.AnyError);
            watcher.AssertError("Rozmer", PropertyErrorWatcher.AnyError);
        }

        [TestMethod]
        public void PriZmenePolicekSeVyvolavaValidatorAPropertyChanged()
        {
            DefinovatNaradiValidace validace = null;
            _validator
                .Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>()))
                .Callback<DefinovatNaradiValidace>(v => validace = v);
            var vm = VytvoritViewModel();
            var pc = new PropertyChangedWatcher(vm);
            vm.Vykres = "557-4711-547";
            vm.Rozmer = "44x20x5mm";
            vm.Druh = "Bruska";
            pc.AssertChange("Vykres");
            pc.AssertChange("Rozmer");
            pc.AssertChange("Druh");
            _repo.VerifyAll();
            Assert.AreEqual("557-4711-547", validace.Vykres, "Validace.Vykres");
            Assert.AreEqual("44x20x5mm", validace.Rozmer, "Validace.Vykres");
            Assert.AreEqual("Bruska", validace.Druh, "Validace.Vykres");
        }

        [TestMethod]
        public void PriNeuspesneValidaciSeDeaktivujeTlacitkoAUPolicekSeZobraziChyby()
        {
            _validator.Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>()));
            var vm = VytvoritViewModel();
            vm.Vykres = "557-4711-547";
            vm.Rozmer = "44x20x5mm";
            vm.Druh = "Bruska";
            var watcher = new PropertyErrorWatcher(vm, "Vykres", "Rozmer", "Druh");
            vm.Handle(
                new UiMessages.ValidovanoDefinovatNaradi()
                .Chyba("Vykres", "Kombinace není unikátní")
                .Chyba("Rozmer", "Kombinace není unikátní")
                );
            watcher.AssertError("Vykres", PropertyErrorWatcher.AnyError);
            watcher.AssertError("Rozmer", PropertyErrorWatcher.AnyError);
            Assert.IsFalse(vm.DefinovatNaradiCommand.CanExecute(null), "Command.Enabled");
            _repo.VerifyAll();
        }

        [TestMethod]
        public void PriUspesneValidaciSeAktivujeTlacitkoAUPolicekSeSmazouChyby()
        {
            _validator.Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>()));
            var vm = VytvoritViewModel();
            vm.Vykres = "557-4711-547";
            vm.Rozmer = "44x20x5mm";
            vm.Druh = "Bruska";
            var watcher = new PropertyErrorWatcher(vm, "Vykres", "Rozmer", "Druh");
            vm.Handle(new UiMessages.ValidovanoDefinovatNaradi());
            watcher.AssertError("Vykres", PropertyErrorWatcher.NoError);
            watcher.AssertError("Rozmer", PropertyErrorWatcher.NoError);
            watcher.AssertError("Druh", PropertyErrorWatcher.NoError);
            Assert.IsTrue(vm.DefinovatNaradiCommand.CanExecute(null), "Command.Enabled");
            _repo.VerifyAll();
        }

        [TestMethod]
        public void PriKliknutiNaProvedeniSeOdeslePrikaz()
        {
            var task = new TaskCompletionSource<object>();
            DefinovatNaradiCommand cmd = null;
            _validator.Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>()));
            _writeSvc
                .Setup(s => s.DefinovatNaradi(It.IsAny<DefinovatNaradiCommand>()))
                .Returns<DefinovatNaradiCommand>(c => { cmd = c; return task.Task; });
            var vm = VytvoritViewModel();
            vm.Vykres = "557-4711-547";
            vm.Rozmer = "50x20x5mm";
            vm.Druh = "Brusný kotouč";
            vm.Handle(new UiMessages.ValidovanoDefinovatNaradi());
            vm.DefinovatNaradiCommand.Execute(null);
            Assert.IsFalse(vm.DefinovatNaradiCommand.CanExecute(null), "Command.Enabled");
            Assert.IsFalse(vm.PovolenyUpravy, "PovolenyUpravy");
            _repo.VerifyAll();
        }

        [TestMethod]
        public void PriDokonceniPrikazuSeOdesleUdalostDoAggregatoru()
        {
            UnitTestingTaskScheduler.RunTest(ts =>
            {
                var task = new TaskCompletionSource<object>();
                DefinovatNaradiCommand cmd = null;
                _validator.Setup(v => v.Zkontrolovat(It.IsAny<DefinovatNaradiValidace>()));
                _writeSvc
                    .Setup(s => s.DefinovatNaradi(It.IsAny<DefinovatNaradiCommand>()))
                    .Returns<DefinovatNaradiCommand>(c => { cmd = c; return task.Task; });
                _bus.Setup(b => b.Publish(It.IsAny<UiMessages.DokoncenaDefiniceNaradi>()));
                var vm = VytvoritViewModel();
                vm.Vykres = "557-4711-547";
                vm.Rozmer = "50x20x5mm";
                vm.Druh = "Brusný kotouč";
                vm.Handle(new UiMessages.ValidovanoDefinovatNaradi());
                vm.DefinovatNaradiCommand.Execute(null);
                task.SetResult(null);
                ts.TryToCompleteTasks(1000);
                _repo.VerifyAll();
            });
        }

        private DefinovatNaradiViewModel VytvoritViewModel()
        {
            return new DefinovatNaradiViewModel(_validator.Object, _writeSvc.Object, _bus.Object);
        }
    }
}
