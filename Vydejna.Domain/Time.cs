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
        void Delay(int milliseconds, CancellationToken cancel, Action onTimer);
    }

    public class RealTime : ITime
    {
        public DateTime GetTime()
        {
            return DateTime.Now;
        }

        public void Delay(int milliseconds, CancellationToken cancel, Action onTimer)
        {
            new DelayHandler(milliseconds, cancel, onTimer);
        }

        private class DelayHandler : IDisposable
        {
            private CancellationToken _cancel;
            private Action _onTimer;
            private Timer _timer;
            private CancellationTokenRegistration _registration;

            public DelayHandler(int milliseconds, CancellationToken cancel, Action onTimer)
            {
                _cancel = cancel;
                _onTimer = onTimer;
                _registration = cancel.Register(Dispose);
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
                    _registration.Dispose();
                    _timer.Dispose();
                }
            }

            public void Dispose()
            {
                _registration.Dispose();
                _timer.Dispose();
            }

        }
    }
}
