using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vydejna.Domain;

namespace Vydejna.Tests.SystemTests
{
    [TestClass]
    public class SystemTest
    {
        [TestMethod, TestCategory("Slow")]
        public void RunDomainServer()
        {
            var bootstrap = new Bootstrap();
            bootstrap.Init();
            bootstrap.WaitForExit();
            bootstrap.Dispose();
        }
    }
}
