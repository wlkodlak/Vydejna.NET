using log4net;
using log4net.Core;
using System;
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using log4net.Util;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class LogMethod : IDisposable
    {
        private ILog _log;
        private string _methodName;
        private Stopwatch _stopwatch;

        public LogMethod(ILog log, string methodName, params object[] parameters)
        {
            _log = log;
            _methodName = methodName;
            
            _log.TraceFormat(">> {0}", _methodName);
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _log.TraceFormat("<< {0} ({1} ms)", _methodName, _stopwatch.ElapsedMilliseconds);
        }
    }

    public static class LogExtensions
    {
        public static bool IsTraceEnabled(this ILog log)
        {
            return log.Logger.IsEnabledFor(_traceLevel);
        }
        public static void Trace(this ILog log, string message)
        {
            var logger = log.Logger;
            if (!logger.IsEnabledFor(_traceLevel))
                return;
            logger.Log(_declaringType, _traceLevel, message, null);
        }
        public static void Trace(this ILog log, string message, Exception exception)
        {
            var logger = log.Logger;
            if (!logger.IsEnabledFor(_traceLevel))
                return;
            logger.Log(_declaringType, _traceLevel, message, exception);
        }
        public static void TraceFormat(this ILog log, string format, params object[] parameters)
        {
            var logger = log.Logger;
            if (!logger.IsEnabledFor(_traceLevel))
                return;
            logger.Log(_declaringType, _traceLevel, new SystemStringFormat(CultureInfo.InvariantCulture, format, parameters), null);
        }

        public static void TraceSql(this ILog log, NpgsqlCommand dbCommand)
        {
            var logger = log.Logger;
            if (!logger.IsEnabledFor(_traceLevel))
                return;
            var sb = new StringBuilder();
            sb.Append("SQL: ").Append(dbCommand.CommandText);
            foreach (NpgsqlParameter dbParam in dbCommand.Parameters)
            {
                sb.Append(", :").Append(dbParam.ParameterName).Append("=");
                if (dbParam.Value == null)
                {
                    sb.Append("NULL");
                }
                else if ((dbParam.NpgsqlDbType & NpgsqlDbType.Array) == 0)
                {
                    var value = dbParam.Value.ToString();
                    if (value.Length <= 200)
                        sb.Append(value);
                    else
                        sb.Append(value.Substring(0, 200));
                }
                else
                {
                    var array = dbParam.Value as Array;
                    sb.Append(array.Length).Append(" elements");
                }
            }
            logger.Log(_declaringType, _traceLevel, sb, null);
        }

        private static Level _traceLevel = Level.Trace;
        private static Type _declaringType = typeof(LogExtensions);
    }
}
