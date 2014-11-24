using log4net;
using log4net.Core;
using System;
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using log4net.Util;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class LogMethod : IDisposable
    {
        private IMethodLogger _log;
        private string _methodName;
        private Stopwatch _stopwatch;

        private interface IMethodLogger
        {
            void Enter(string methodName);
            void Exit(string methodName, long ms);
        }

        private class MethodLoggerLog4Net : IMethodLogger
        {
            private ILog _log;

            public MethodLoggerLog4Net(ILog log)
            {
                _log = log;
            }

            public void Enter(string methodName)
            {
                _log.TraceFormat(">> {0}", methodName);
            }

            public void Exit(string methodName, long ms)
            {
                _log.TraceFormat("<< {0} ({1} ms)", methodName, ms);
            }
        }

        private class MethodLoggerTraceSource : IMethodLogger
        {
            private TraceSource _log;

            public MethodLoggerTraceSource(TraceSource log)
            {
                _log = log;
            }

            public void Enter(string methodName)
            {
                _log.TraceEvent(TraceEventType.Start, 1, ">> {0}", methodName);
            }

            public void Exit(string methodName, long ms)
            {
                _log.TraceEvent(TraceEventType.Stop, 2, "<< {0} ({1} ms)", methodName, ms);
            }
        }

        public LogMethod(ILog log, string methodName, params object[] parameters)
            : this(new MethodLoggerLog4Net(log), methodName)
        {
        }

        public LogMethod(TraceSource log, string methodName)
            : this(new MethodLoggerTraceSource(log), methodName)
        {
        }

        private LogMethod(IMethodLogger log, string methodName)
        {
            _log = log;
            _methodName = methodName;

            _log.Enter(_methodName);
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _log.Exit(_methodName, _stopwatch.ElapsedMilliseconds);
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
            var sb = GenerateTraceSqlMessage(dbCommand);
            logger.Log(_declaringType, _traceLevel, sb, null);
        }

        public static void TraceSql(this TraceSource log, ILogContext logContext, NpgsqlCommand dbCommand)
        {
            if (!log.Switch.ShouldTrace(TraceEventType.Verbose))
                return;
            new LogContextTraceSqlMessage(dbCommand).Log(log, logContext);
        }

        private static StringBuilder GenerateTraceSqlMessage(NpgsqlCommand dbCommand)
        {
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
            return sb;
        }

        private static Level _traceLevel = Level.Trace;
        private static Type _declaringType = typeof(LogExtensions);
    }

    public class LogContextTraceSqlMessage : ILogContextMessage
    {
        private string _commandText;
        private Dictionary<string, string> _parameters;
        private ILogContext _logContext;

        public LogContextTraceSqlMessage(NpgsqlCommand cmd)
        {
            _commandText = cmd.CommandText;
            _parameters = new Dictionary<string, string>();
            foreach (NpgsqlParameter dbParam in cmd.Parameters)
            {
                _parameters[dbParam.ParameterName] = ToPrintableValue(dbParam);
            }
        }

        private string ToPrintableValue(NpgsqlParameter dbParam)
        {
            if (dbParam.Value == null)
            {
                return "NULL";
            }
            else if ((dbParam.NpgsqlDbType & NpgsqlDbType.Array) == 0)
            {
                var value = dbParam.Value.ToString();
                if (value.Length <= 200)
                    return value;
                else
                    return value.Substring(0, 200);
            }
            else
            {
                var array = dbParam.Value as Array;
                return string.Concat(array.Length, " elements");
            }
        }

        public TraceEventType Level
        {
            get { return TraceEventType.Verbose; }
        }

        public string SummaryFormat
        {
            get { return "Executing SQL: {CommandText}"; }
        }

        public ILogContext LogContext
        {
            get { return _logContext; }
        }

        public object GetProperty(string name)
        {
            string parameterValue;
            if (string.Equals(name, "CommandText", StringComparison.Ordinal))
                return _commandText;
            else if (_parameters.TryGetValue(name, out parameterValue))
                return parameterValue;
            else
                return null;
        }

        public IEnumerator<LogContextMessageProperty> GetEnumerator()
        {
            var list = new List<LogContextMessageProperty>();
            list.Add(new LogContextMessageProperty { Name = "CommandText", IsLong = false, Value = _commandText });
            foreach (var parameter in _parameters)
            {
                list.Add(new LogContextMessageProperty { Name = parameter.Key, IsLong = false, Value = parameter.Value });
            }
            return list.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Log(TraceSource log, ILogContext logContext)
        {
            _logContext = logContext;
            log.TraceData(Level, 10001, this);
        }
    }
}
