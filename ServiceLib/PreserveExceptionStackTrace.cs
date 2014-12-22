using System;
using System.Reflection;

namespace ServiceLib
{
    public static class PreserveExceptionStackTraceExtensions
    {
        private static readonly Action<Exception> _preserveStack =
            (Action<Exception>) Delegate.CreateDelegate(
                typeof (Action<Exception>),
                typeof (Exception).GetMethod(
                    "InternalPreserveStackTrace",
                    BindingFlags.Instance | BindingFlags.NonPublic));

        public static Exception PreserveStackTrace(this Exception exception)
        {
            _preserveStack(exception);
            return exception;
        }
    }
}