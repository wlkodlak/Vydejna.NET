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
        IDisposable Delay(int milliseconds, Action onTimer);
    }

    public class RealTime : ITime
    {
        public DateTime GetUtcTime()
        {
            return DateTime.UtcNow;
        }

        public IDisposable Delay(int milliseconds, Action onTimer)
        {
            return new DelayHandler(milliseconds, onTimer);
        }

        private class DelayHandler : IDisposable
        {
            private Action _onTimer;
            private Timer _timer;

            public DelayHandler(int milliseconds, Action onTimer)
            {
                _onTimer = onTimer;
                _timer = new Timer(Callback, null, milliseconds, Timeout.Infinite);
            }

            private void Callback(object state)
            {
                try
                {
                    _onTimer();
                }
                catch
                {
                }
                finally
                {
                    _timer.Dispose();
                }
            }

            public void Dispose()
            {
                _timer.Dispose();
            }

        }
    }
}
