using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface ITime
    {
        DateTime GetTime();
        Task Delay(int milliseconds, CancellationToken cancel);
    }

    public class RealTime : ITime
    {
        public DateTime GetTime()
        {
            return DateTime.Now;
        }

        public Task Delay(int milliseconds, CancellationToken cancel)
        {
            return Task.Delay(milliseconds, cancel);
        }
    }
}
