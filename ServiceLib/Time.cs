using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface ITime
    {
        DateTime GetUtcTime();
        Task Delay(int milliseconds, CancellationToken cancel);
    }

    public class RealTime : ITime
    {
        public DateTime GetUtcTime()
        {
            return DateTime.UtcNow;
        }

        public Task Delay(int milliseconds, CancellationToken cancel)
        {
            return TaskUtils.Delay(milliseconds, cancel);
        }
    }
}
