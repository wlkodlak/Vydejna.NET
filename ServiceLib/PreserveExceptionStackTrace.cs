using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public static class PreserveExceptionStackTraceExtensions
    {
        private static Action<Exception> _preserveStack = 
            (Action<Exception>)Delegate.CreateDelegate(typeof(Action<Exception>), 
            typeof(Exception).GetMethod("InternalPreserveStackTrace",
            BindingFlags.Instance | BindingFlags.NonPublic));

        public static Exception PreserveStackTrace(this Exception exception)
        {
            _preserveStack(exception);
            return exception;
        }
    }
}
