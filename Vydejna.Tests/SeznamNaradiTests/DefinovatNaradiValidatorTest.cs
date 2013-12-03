using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Gui.Common;
using Vydejna.Gui.SeznamNaradi;
using Vydejna.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class DefinovatNaradiValidatorTest
    {
        private class EventAggregator : IEventPublisher
        {
            public List<object> Udalosti = new List<object>();
            public void Publish<T>(T msg)
            {
                Udalosti.Add(msg);
            }
        }

        private EventAggregator _vysledky;
        private Mock<IReadSeznamNaradi> _readSvc;

        [TestInitialize]
        public void Initialize()
        {
            _vysledky = new EventAggregator();
            _readSvc = new Mock<IReadSeznamNaradi>();
        }

        [TestMethod]
        public void ChybejiciVykresOznamiOkamzite()
        {
            var validator = VytvoritValidator();
            validator.Zkontrolovat(new DefinovatNaradiValidace { Vykres = "", Rozmer = "", Druh = "" });
            OveritChybu("Vykres", s => !string.IsNullOrEmpty(s));
            OveritChybu("Rozmer", s => !string.IsNullOrEmpty(s));
            OveritChybu("Druh", s => true);
        }

        [TestMethod]
        public void KombinaceRozmeruAVykresuNeniUnikatni()
        {
            UnitTestingTaskScheduler.RunTest(ts =>
            {
                var task = new TaskCompletionSource<OvereniUnikatnostiDto>();
                _readSvc.Setup(s => s.OveritUnikatnost("5847-5584-b", "50x20x5")).Returns(task.Task).Verifiable();
                var validator = VytvoritValidator();
                validator.Zkontrolovat(new DefinovatNaradiValidace { Vykres = "5847-5584-b", Rozmer = "50x20x5", Druh = "Brusný kotouč" });
                task.SetResult(new OvereniUnikatnostiDto { Vykres = "5847-5584-b", Rozmer = "50x20x5", Existuje = true });
                ts.TryToCompleteTasks(1000);
                OveritChybu("Vykres", s => !string.IsNullOrEmpty(s));
                OveritChybu("Rozmer", s => !string.IsNullOrEmpty(s));
                OveritChybu("Druh", s => true);
            });
        }

        [TestMethod]
        public void ValidaceVPoradku()
        {
            UnitTestingTaskScheduler.RunTest(ts =>
            {
                var task = new TaskCompletionSource<OvereniUnikatnostiDto>();
                _readSvc.Setup(s => s.OveritUnikatnost("5847-5584-b", "50x20x5")).Returns(task.Task).Verifiable();
                var validator = VytvoritValidator();
                validator.Zkontrolovat(new DefinovatNaradiValidace { Vykres = "5847-5584-b", Rozmer = "50x20x5", Druh = "Brusný kotouč" });
                task.SetResult(new OvereniUnikatnostiDto { Vykres = "5847-5584-b", Rozmer = "50x20x5", Existuje = false });
                ts.TryToCompleteTasks(1000);
                Assert.AreEqual(0, SeznamChyb().Count, "Nemá obsahovat chyby");
            });
        }

        private IDefinovatNaradiValidator VytvoritValidator()
        {
            return new DefinovatNaradiValidator(_vysledky, _readSvc.Object);
        }

        private UiMessages.ValidovanoDefinovatNaradi ZiskatVysledek()
        {
            var validace = _vysledky.Udalosti.OfType<UiMessages.ValidovanoDefinovatNaradi>().ToList();
            if (validace.Count == 0)
                Assert.Fail("Žádné výsledky validací nejsou k dispozici");
            else if (validace.Count > 1)
                Assert.Fail("Je povolen pouze jediný výsledek validace");
            return validace[0];
        }

        private void OveritChybu(string polozka, Predicate<string> overeniTextu, string message = null)
        {
            var chyba = SeznamChyb().Where(ch => ch.Polozka == polozka).FirstOrDefault();
            string text = null;
            bool nemaChybu = false;
            if (chyba == null)
            {
                text = string.Empty;
                nemaChybu = true;
            }
            else if (string.IsNullOrEmpty(chyba.Chyba))
                Assert.Fail(string.Format("Položka {0} obsahuje prázdnou chybu", polozka));
            else
                text = chyba.Chyba;

            var overeno = overeniTextu(text);
            if (!overeno)
            {
                var sb = new StringBuilder();
                if (nemaChybu)
                    sb.AppendFormat("Polozka {0} ma mit chybu.", polozka);
                else
                    sb.AppendFormat("Polozka {0} ma spatnou chybu {1}.", polozka, text);
                if (message != null)
                    sb.Append(Environment.NewLine).Append(message);
                Assert.Fail(sb.ToString());
            }
        }

        private List<UiMessages.ChybaValidaceDefinovatNaradi> SeznamChyb()
        {
            return ZiskatVysledek().Chyby;
        }
    }
}
