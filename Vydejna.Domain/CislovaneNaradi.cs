using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class CislovaneNaradi : EventSourcedAggregate
    {
        protected override void DispatchEvent(object evt)
        {
        }

        public void Execute(CislovaneNaradiPrijmoutNaVydejnuCommand cmd)
        {
            if (cmd.CenaNova < 0)
                throw new ArgumentOutOfRangeException("CenaNova", "Cena nesmi byt zaporna");
        }
    }
}
