using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface IBus
    {
        void Publish(IList<object> messages);
    }

    public static class SystemEvents
    {
        public class SystemInit { }
        public class SystemStarted { }
        public class SystemShutdown { }
    }
}
