using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Domain;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class SeznamNaradiProjectionTest
    {
        private DocumentStoreInMemory _store;
        private IProjectionProcess _process;

        private class FakeProjectionProcess : IProjectionProcess
        {
            public Task CommitProjectionProgress()
            {
                return TaskResult.GetCompletedTask();
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            _store = new DocumentStoreInMemory();
            _process = new FakeProjectionProcess();
        }

        private SeznamNaradiProjection VytvoritProjection(string instanceName = "A")
        {
            var projekce = new SeznamNaradiProjection(_store, "SeznamNaradi");
            var iprojection = projekce as IProjection;
            iprojection.SetInstanceName(instanceName).GetAwaiter().GetResult();
            iprojection.SetProcessServices(_process);
            return projekce;
        }

        private static SeznamNaradiDto ZiskatVsechnoNaradi(SeznamNaradiProjection proj)
        {
            return proj.NacistSeznamNaradi(0, int.MaxValue).GetAwaiter().GetResult();
        }

        private static Guid Id(int number)
        {
            return new Guid(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void HandleDefinovanoNaradi_ZachovavaRazeni()
        {
            var proj = VytvoritProjection();

            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(284), Vykres = "Vykres-09-34812", Rozmer = "5x100", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(199), Vykres = "Vykres-07-87654", Rozmer = "o 350", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(924), Vykres = "Vykres-06-55824", Rozmer = "o 475", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });

            var seznam = ZiskatVsechnoNaradi(proj);
            Assert.AreEqual(8, seznam.PocetCelkem, "PocetCelkem");
            var serazeneGuidy = new Guid[] { Id(101), Id(571), Id(124), Id(924), Id(199), Id(284), Id(335), Id(140) };
            var guidyVysledku = seznam.SeznamNaradi.Select(n => n.Id).ToArray();
            AssertGuidCollectionsEqual(serazeneGuidy, guidyVysledku, "Poradi");
        }

        [TestMethod]
        public void HandleDeaktivovanoNaradi_UpraviExistujici()
        {
            var proj = VytvoritProjection();
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });

            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });

            var seznam = ZiskatVsechnoNaradi(proj);
            var aktivniGuid = seznam.SeznamNaradi.Where(n => n.Aktivni).Select(n => n.Id).ToArray();
            var neaktivniGuid = seznam.SeznamNaradi.Where(n => !n.Aktivni).Select(n => n.Id).ToArray();
            var aktivniOcekavane = new Guid[] { Id(101), Id(124), Id(335) };
            var neaktivniOcekavane = new Guid[] { Id(571), Id(140) };
            AssertGuidCollectionsEqual(aktivniOcekavane, aktivniGuid, "Aktivni");
            AssertGuidCollectionsEqual(neaktivniOcekavane, neaktivniGuid, "Neaktivni");
        }

        [TestMethod]
        public void HandleAktivovanoNaradi_UpraviExistujici()
        {
            var proj = VytvoritProjection();
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });

            proj.Handle(new AktivovanoNaradiEvent() { NaradiId = Id(140) });

            var seznam = ZiskatVsechnoNaradi(proj);
            var aktivniGuid = seznam.SeznamNaradi.Where(n => n.Aktivni).Select(n => n.Id).ToArray();
            var neaktivniGuid = seznam.SeznamNaradi.Where(n => !n.Aktivni).Select(n => n.Id).ToArray();
            var aktivniOcekavane = new Guid[] { Id(124), Id(335), Id(140) };
            var neaktivniOcekavane = new Guid[] { Id(101), Id(571) };
            AssertGuidCollectionsEqual(aktivniOcekavane, aktivniGuid, "Aktivni");
            AssertGuidCollectionsEqual(neaktivniOcekavane, neaktivniGuid, "Neaktivni");
        }

        [TestMethod]
        public void OvereniUnikanosti_AktivniINeaktivni()
        {
            var proj = VytvoritProjection();
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            proj.Handle(new AktivovanoNaradiEvent() { NaradiId = Id(140) });

            TestOvereniUnikatnosti(proj, "Vykres-02-37214", "o 4.2", true);
            TestOvereniUnikatnosti(proj, "Vykres-12-37512", "o 100", true);
            TestOvereniUnikatnosti(proj, "Vykres-02-37214", "", false);
            TestOvereniUnikatnosti(proj, "Vykres-99-37214", "o 4.2", false);
        }

        private void TestOvereniUnikatnosti(SeznamNaradiProjection proj, string vykres, string rozmer, bool existuje)
        {
            var dto = proj.OveritUnikatnost(vykres, rozmer).GetAwaiter().GetResult();
            Assert.AreEqual(existuje, dto.Existuje, "Existence {0} {1}", vykres, rozmer);
            Assert.AreEqual(vykres, dto.Vykres, "Vykres {0}", vykres);
            Assert.AreEqual(rozmer, dto.Rozmer, "Rozmer {0}", rozmer);
        }

        [TestMethod]
        public void ZmenyJsouPersistentni()
        {
            var proj = VytvoritProjection();
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            proj.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            proj.Handle(new AktivovanoNaradiEvent() { NaradiId = Id(140) });
            ProjectionShutdown(proj);
            SafeHandle(proj, new SystemEvents.SystemShutdown());

            var proj2 = VytvoritProjection();
            TestOvereniUnikatnosti(proj2, "Vykres-12-37512", "o 100", true);
            TestOvereniUnikatnosti(proj2, "Vykres-02-37214", "", false);
            var seznam = ZiskatVsechnoNaradi(proj2);
            var aktivniGuid = seznam.SeznamNaradi.Where(n => n.Aktivni).Select(n => n.Id).ToArray();
            var aktivniOcekavane = new Guid[] { Id(124), Id(335), Id(140) };
            AssertGuidCollectionsEqual(aktivniOcekavane, aktivniGuid, "Aktivni");
        }

        [TestMethod]
        public void ParalelniProjekce()
        {
            var proj1 = VytvoritProjection("A");
            proj1.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj1.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj1.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj1.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            proj1.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            proj1.Handle(new AktivovanoNaradiEvent() { NaradiId = Id(140) });
            ProjectionShutdown(proj1);

            var proj2 = VytvoritProjection("B");
            proj2.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            proj2.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            proj2.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            proj2.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            proj2.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            proj2.Handle(new AktivovanoNaradiEvent() { NaradiId = Id(140) });
            proj2.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            proj2.Handle(new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            proj2.Handle(new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            ProjectionShutdown(proj2);

            var aktivni1Expect = new Guid[] { Id(571), Id(140)  };
            var aktivni1Real = ZiskatVsechnoNaradi(proj1).SeznamNaradi.Where(n => n.Aktivni).Select(n => n.Id).ToArray();
            AssertGuidCollectionsEqual(aktivni1Expect, aktivni1Real, "Aktivni1");

            var aktivni2Expect = new Guid[] { Id(124), Id(335), Id(140) };
            var aktivni2Real = ZiskatVsechnoNaradi(proj2).SeznamNaradi.Where(n => n.Aktivni).Select(n => n.Id).ToArray();
            AssertGuidCollectionsEqual(aktivni2Expect, aktivni2Real, "Aktivni2");
        }

        private void ProjectionShutdown(IProjection projection)
        {
            projection.HandleShutdown().GetAwaiter().GetResult();
        }

        private void SafeHandle<T>(object proj, T evt)
        {
            var handler = proj as IHandle<T>;
            if (handler != null)
                handler.Handle(evt);
        }

        private void AssertGuidCollectionsEqual(IList<Guid> expected, IList<Guid> actual, string message)
        {
            string errorMessage = null;
            if (expected.Count != actual.Count)
                errorMessage = string.Format("{0}: Different counts", message);
            else
            {
                for (int i = 0; i < expected.Count; i++)
                {
                    if (!object.Equals(expected[i], actual[i]))
                    {
                        errorMessage = string.Format("{1}: Difference at {0}", i, message);
                        break;
                    }
                }
            }

            if (errorMessage != null)
            {
                Assert.Fail("{0}\r\nExpected: {1}\r\nActual: {2}",
                    errorMessage,
                    string.Join(", ", expected.Select(g => g.ToString().Substring(0, 8)).ToArray()),
                    string.Join(", ", actual.Select(g => g.ToString().Substring(0, 8)).ToArray()));
            }
        }
    }
}
