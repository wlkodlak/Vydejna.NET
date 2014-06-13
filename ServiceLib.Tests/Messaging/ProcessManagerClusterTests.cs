using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ServiceLib.Tests.TestUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.Messaging
{
    [TestClass]
    public class ProcessManagerClusterTests
    {
        private VirtualTime _time;
        private QueuedBus _bus;
        private ProcessManagerCluster _mgr;
        private QueuedBusProcess _busProcess;

        [TestInitialize]
        public void Initialize()
        {
            _time = new VirtualTime();
            _bus = new QueuedBus(new SubscriptionManager(), "TestBus");
            _mgr = new ProcessManagerCluster(_time, "testNode", _bus);
            _busProcess = new QueuedBusProcess(_bus);
            _mgr.RegisterBus("TestBus", _bus, _busProcess);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _mgr.Stop();
            _mgr.WaitForStop();
        }

        /*
         * Pri startu se odesila ElectionsInquiry
         * Pokud na ElectionsInquiry nekdo s vyssim ID odpovi ElectionsCandidate, tento uzel nebude leaderem
         * Pokud po ElectionsInquiry nekdo s vyssim ID odpovi ElectionsLeader, tento uzel nebude leaderem
         * Pokud na ElectionsInquiry nikdo s vyssim ID neodpovi, tento uzel bude leaderem
         * 
         * Po startu se spousti lokalni procesy
         * Lokalni proces lze zadosti ukoncit
         * Lokalni proces lze zadosti opet spustit
         * Pri konci se ukoncuji lokalni procesy
         * 
         * Pri ProcessStart se spousti globalni proces a vysledkem je pozdeji odpoved ProcessChanged
         * Pri ProcessStop se ukoncuje globalni proces a vysledkem je pozdeji odpoved ProcessChanged
         */
    }
}
