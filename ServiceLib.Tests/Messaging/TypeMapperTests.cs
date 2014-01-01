using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class TypeMapperTests
    {
        [TestMethod]
        public void RegisterManually()
        {
            var map = new TypeMapper();
            map.Register(typeof(SystemEvents.SystemInit), "SystemEvents.Initializing");
            map.Register(typeof(SystemEvents.SystemShutdown), "SystemEvents.Shutdown");
            Assert.AreEqual("SystemEvents.Initializing", map.GetName(typeof(SystemEvents.SystemInit)));
            Assert.AreEqual(typeof(SystemEvents.SystemShutdown), map.GetType("SystemEvents.Shutdown"));
        }

        [TestMethod]
        public void RegisterGeneric()
        {
            var map = new TypeMapper();
            map.Register<SystemEvents.SystemInit>();
            map.Register<SystemEvents.SystemShutdown>();
            Assert.AreEqual("ServiceLib.SystemEvents.SystemInit", map.GetName(typeof(SystemEvents.SystemInit)));
            Assert.AreEqual(typeof(SystemEvents.SystemShutdown), map.GetType("ServiceLib.SystemEvents.SystemShutdown"));
        }
    }
}
