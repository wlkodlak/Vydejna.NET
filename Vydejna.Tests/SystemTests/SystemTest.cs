using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Domain;

namespace Vydejna.Tests.SystemTests
{
    [TestClass]
    public class SystemTest
    {
        [TestMethod]
        public void RunDomainServer()
        {
            var bootstrap = new Bootstrap();
            bootstrap.Init();
            System.Threading.Thread.Sleep(1000);
            bootstrap.Stop();
            bootstrap.Dispose();
        }
    }
}
