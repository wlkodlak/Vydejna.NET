using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Vydejna.Contracts;
using Vydejna.Gui;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ServiceStack.Text;
using Vydejna.Tests.HttpTests;
using Vydejna.Gui.SeznamNaradi;

namespace Vydejna.Tests.SeznamNaradiTests
{
    [TestClass]
    public class ReadSeznamNaradiClientTest
    {
        [TestMethod]
        public void ZiskatSeznamNaradi()
        {
            var responseDto = new SeznamNaradiDto() { Offset = 40, PocetCelkem = 48 };
            for (int i = 0; i < 8; i++)
                responseDto.SeznamNaradi.Add(new TypNaradiDto(Guid.NewGuid(), "001-" + i, i + "x" + i, "", true));

            var tester = new RestClientTester();
            tester.ExpectedMethod = "GET";
            tester.ExpectedUrl = "http://localhost:4472/SeznamNaradi?offset=40&pocet=20";
            tester.PreparedResponse = new HttpClientResponseBuilder()
                .WithHeader("Content-Type", "application/json")
                .WithStatus(200)
                .WithStringPayload(JsonSerializer.SerializeToString(responseDto))
                .Build();

            var svc = new ReadSeznamNaradiClient("http://localhost:4472/", tester.HttpClient);
            var returnedDto = tester.RunTest(() => svc.NacistSeznamNaradi(40, 20));

            CollectionAssert.AreEqual(new byte[0], tester.Request.Body, "Body");

            Assert.IsNotNull(returnedDto, "No response returned");
            Assert.AreEqual(40, returnedDto.Offset, "Offset");
            Assert.AreEqual(48, returnedDto.PocetCelkem, "PocetCelkem");
            Assert.AreEqual(8, returnedDto.SeznamNaradi.Count, "SeznamNaradi.Count");
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(responseDto.SeznamNaradi[i].Id, returnedDto.SeznamNaradi[i].Id, "SeznamNaradi[{0}].Id", i);
                Assert.AreEqual(responseDto.SeznamNaradi[i].Vykres, returnedDto.SeznamNaradi[i].Vykres, "SeznamNaradi[{0}].Vykres", i);
                Assert.AreEqual(responseDto.SeznamNaradi[i].Rozmer, returnedDto.SeznamNaradi[i].Rozmer, "SeznamNaradi[{0}].Rozmer", i);
                Assert.AreEqual(responseDto.SeznamNaradi[i].Druh, returnedDto.SeznamNaradi[i].Druh, "SeznamNaradi[{0}].Druh", i);
                Assert.AreEqual(responseDto.SeznamNaradi[i].Aktivni, returnedDto.SeznamNaradi[i].Aktivni, "SeznamNaradi[{0}].Aktivni", i);
            }
        }

        [TestMethod]
        public void ZiskatUnikatnostNaradi()
        {
            var responseDto = new OvereniUnikatnostiDto()
            {
                Vykres = "578-5577-25",
                Rozmer = "450x20x20",
                Existuje = true
            };

            var tester = new RestClientTester();
            tester.ExpectedMethod = "GET";
            tester.ExpectedUrl = "http://localhost:4472/OveritUnikatnost?vykres=578-5577-25&rozmer=450x20x20";
            tester.PreparedResponse = new HttpClientResponseBuilder()
                .WithHeader("Content-Type", "application/json")
                .WithStatus(200)
                .WithStringPayload(JsonSerializer.SerializeToString(responseDto))
                .Build();

            var svc = new ReadSeznamNaradiClient("http://localhost:4472/", tester.HttpClient);
            var returnedDto = tester.RunTest(() => svc.OveritUnikatnost("578-5577-25", "450x20x20"));
            CollectionAssert.AreEqual(new byte[0], tester.Request.Body, "Body");

            Assert.IsNotNull(returnedDto, "No response returned");
            Assert.AreEqual("578-5577-25", returnedDto.Vykres, "Vykres");
            Assert.AreEqual("450x20x20", returnedDto.Rozmer, "Rozmer");
            Assert.AreEqual(true, returnedDto.Existuje, "Existuje");
        }


    }
}
