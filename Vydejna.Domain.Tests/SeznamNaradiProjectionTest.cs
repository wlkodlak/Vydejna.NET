using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Domain.Tests
{
    [TestClass]
    public class SeznamNaradiProjectionTest
    {
        private TestExecutor _executor;
        private TestDocumentFolder _folder;
        private TestStreaming _streaming;
        private VirtualTime _time;

        private SeznamNaradiProjection _projekce;
        private SeznamNaradiReader _reader;
        
        [TestInitialize]
        public void Initialize()
        {
            _executor = new TestExecutor();
            _folder = new TestDocumentFolder(_executor);
            _streaming = new TestStreaming(_executor);
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2014, 2, 18, 14, 33, 20));
            var repository = new SeznamNaradiRepository(_folder);
            _projekce = new SeznamNaradiProjection(repository, _executor, _time);
            _reader = new SeznamNaradiReader(repository, _executor, _time);
            var metadata = new TestMetadataInstance();
            var subscriptions = new QueuedCommandSubscriptionManager(new CommandSubscriptionManager(), _executor);
            _projekce.Subscribe(subscriptions);
            var proces = new EventProjectorSimple(_projekce, metadata, _streaming, subscriptions);
            proces.Start();
            metadata.SendLock();
        }

        private ZiskatSeznamNaradiResponse ZiskatPrvniStrankuNaradi()
        {
            ZiskatSeznamNaradiResponse response = null;
            _reader.Handle(new QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(
                new ZiskatSeznamNaradiRequest(1),
                r => response = r, ex => { throw ex.PreserveStackTrace(); }));
            _executor.Process();
            return response;
        }

        private static Guid Id(int number)
        {
            return new Guid(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        [TestMethod]
        public void HandleDefinovanoNaradi_ZachovavaRazeni()
        {
            _streaming.AddEvent("1", new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            _streaming.AddEvent("2", new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            _streaming.AddEvent("3", new DefinovanoNaradiEvent() { NaradiId = Id(284), Vykres = "Vykres-09-34812", Rozmer = "5x100", Druh = "" });
            _streaming.AddEvent("4", new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            _streaming.AddEvent("5", new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            _streaming.AddEvent("6", new DefinovanoNaradiEvent() { NaradiId = Id(199), Vykres = "Vykres-07-87654", Rozmer = "o 350", Druh = "" });
            _streaming.AddEvent("7", new DefinovanoNaradiEvent() { NaradiId = Id(924), Vykres = "Vykres-06-55824", Rozmer = "o 475", Druh = "" });
            _streaming.AddEvent("8", new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            _streaming.MarkEndOfStream();
            _executor.Process();

            var seznam = ZiskatPrvniStrankuNaradi();
            Assert.AreEqual(8, seznam.PocetCelkem, "PocetCelkem");
            var serazeneGuidy = new Guid[] { Id(101), Id(571), Id(124), Id(924), Id(199), Id(284), Id(335), Id(140) };
            var guidyVysledku = seznam.SeznamNaradi.Select(n => n.Id).ToArray();
            AssertGuidCollectionsEqual(serazeneGuidy, guidyVysledku, "Poradi");
        }

        [TestMethod]
        public void HandleDeaktivovanoNaradi_UpraviExistujici()
        {
            _streaming.AddEvent("1", new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            _streaming.AddEvent("2", new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            _streaming.AddEvent("3", new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            _streaming.AddEvent("4", new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            _streaming.AddEvent("5", new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            _streaming.MarkEndOfStream();
            _executor.Process();

            _streaming.AddEvent("6", new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            _streaming.AddEvent("7", new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            _streaming.MarkEndOfStream();
            _executor.Process();

            var seznam = ZiskatPrvniStrankuNaradi();
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
            _streaming.AddEvent("1", new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            _streaming.AddEvent("2", new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            _streaming.AddEvent("3", new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            _streaming.AddEvent("4", new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            _streaming.AddEvent("5", new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            _streaming.AddEvent("6", new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            _streaming.AddEvent("7", new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            _streaming.AddEvent("8", new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            _streaming.MarkEndOfStream();
            _executor.Process();

            _streaming.AddEvent("9", new AktivovanoNaradiEvent() { NaradiId = Id(140) });
            _streaming.MarkEndOfStream();
            _executor.Process();

            var seznam = ZiskatPrvniStrankuNaradi();
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
            _streaming.AddEvent("1", new DefinovanoNaradiEvent() { NaradiId = Id(101), Vykres = "Vykres-01-55824", Rozmer = "o 150", Druh = "" });
            _streaming.AddEvent("2", new DefinovanoNaradiEvent() { NaradiId = Id(571), Vykres = "Vykres-02-37214", Rozmer = "o 4.2", Druh = "" });
            _streaming.AddEvent("3", new DefinovanoNaradiEvent() { NaradiId = Id(124), Vykres = "Vykres-05-11624", Rozmer = "20x20", Druh = "" });
            _streaming.AddEvent("4", new DefinovanoNaradiEvent() { NaradiId = Id(335), Vykres = "Vykres-11-72184", Rozmer = "170x3", Druh = "" });
            _streaming.AddEvent("5", new DefinovanoNaradiEvent() { NaradiId = Id(140), Vykres = "Vykres-12-37512", Rozmer = "o 100", Druh = "" });
            _streaming.AddEvent("6", new DeaktivovanoNaradiEvent() { NaradiId = Id(101) });
            _streaming.AddEvent("7", new DeaktivovanoNaradiEvent() { NaradiId = Id(571) });
            _streaming.AddEvent("8", new DeaktivovanoNaradiEvent() { NaradiId = Id(140) });
            _streaming.AddEvent("9", new AktivovanoNaradiEvent() { NaradiId = Id(140) });
            _streaming.MarkEndOfStream();
            _executor.Process();

            TestOvereniUnikatnosti("Vykres-02-37214", "o 4.2", true);
            TestOvereniUnikatnosti("Vykres-12-37512", "o 100", true);
            TestOvereniUnikatnosti("Vykres-02-37214", "", false);
            TestOvereniUnikatnosti("Vykres-99-37214", "o 4.2", false);
        }

        private void TestOvereniUnikatnosti(string vykres, string rozmer, bool existuje)
        {
            OvereniUnikatnostiResponse dto = null;
            _reader.Handle(new QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(
                new OvereniUnikatnostiRequest(vykres, rozmer),
                r => dto = r, ex => {throw ex.PreserveStackTrace();}));
            _executor.Process();
            Assert.IsNotNull(dto, "Dto");
            Assert.AreEqual(existuje, dto.Existuje, "Existence {0} {1}", vykres, rozmer);
            Assert.AreEqual(vykres, dto.Vykres, "Vykres {0}", vykres);
            Assert.AreEqual(rozmer, dto.Rozmer, "Rozmer {0}", rozmer);
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
                    string.Join(", ", expected.Select(Id).ToArray()),
                    string.Join(", ", actual.Select(Id).ToArray()));
            }
        }

        private static int Id(Guid id)
        {
            int number = 0;
            var bytes = id.ToByteArray();
            for (int i = 4; i > 0; i--)
                number = number * 256 + bytes[i - 1];
            return number;
        }
    }
}
