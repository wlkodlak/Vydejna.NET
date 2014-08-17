using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ServiceLib
{
    public interface ILogContext
    {
        ILogContextAdvanced Advanced { get; }
        void Critical(string origin, string message, Exception exception, params object[] parameters);
        void Error(string origin, string message, Exception exception, params object[] parameters);
        void Error(string origin, string message, params object[] parameters);
        void TransientError(string origin, string message, Exception exception, params object[] parameters);
        void TransientFixed(string origin, string message, params object[] parameters);
        void Failure(string origin, string message, Exception exception, params object[] parameters);
        void Warning(string origin, string message, params object[] parameters);
        void Info(string origin, string message, params object[] parameters);
        void Debug(string origin, string message, params object[] parameters);
        void Trace(string origin, string message, params object[] parameters);
    }
    public interface ILogContextAdvanced
    {
        void Finish();
        IList<ILogContextMessage> GetLogMessages();
        T GetContext<T>();
        void SetContext<T>(T context);
        void Log(ILogContextMessage message);
        string GenerateSummaryFor(object message);
        DateTime CurrentTimestamp();
    }

    public interface ILogContextFactory
    {
        ILogContext Build(string shortContext);
    }

    public interface ILogContextImmediateWriter
    {
        void Write(ILogContextAdvanced context, System.IO.TextWriter writer, ILogContextMessage message);
    }

    public interface ILogContextDelayedWriter
    {
        void Write(ILogContextAdvanced context, System.IO.TextWriter writer);
    }

    public interface ILogContextConfigurable
    {
        void ImmediateLog(ILogContextImmediateWriter writer);
        void When(Predicate<ILogContextAdvanced> condition, int priority, ILogContextDelayedWriter writer);
    }

    public interface ILogContextMessage
    {
        DateTime Timestamp { get; }
        string Origin { get; }
        LogContextLevel Level { get; }
        string Message { get; }
    }

    public enum LogContextLevel
    {
        Unknown,
        Critical,
        Error,
        Transient,
        TransientFixed,
        Failure,
        Warning,
        Information,
        Debug,
        Trace,
        ContextReset
    }

    public class LogContextHttp
    {
        private ILogContextAdvanced _context;
        private Stopwatch _stopwatch;
        private IHttpServerStagedContext _httpContext;
        private Exception _exception;

        private LogContextHttp() { }

        public IHttpServerStagedContext HttpContext { get { return _httpContext; } }
        public Exception Exception { get { return _exception; } }

        public static void Request(ILogContext context, IHttpServerStagedContext httpContext)
        {
            var self = new LogContextHttp();
            self._context = context.Advanced;
            self._stopwatch = new Stopwatch();
            self._httpContext = httpContext;
            self._stopwatch.Start();
            self._context.SetContext(self);
            self._context.Log(new LogContextHttpRequestMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                HttpContext = self._httpContext
            });
        }

        public static void Response(ILogContext context)
        {
            var self = context.Advanced.GetContext<LogContextHttp>();
            self._stopwatch.Stop();
            self._context.Log(new LogContextHttpResponseMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                HttpContext = self._httpContext,
                DurationMs = self._stopwatch.ElapsedMilliseconds
            });
        }

        public static void Crashed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextHttp>();
            self._exception = exception;
            self._stopwatch.Stop();
            self._context.Log(new LogContextHttpCrashedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                HttpContext = self._httpContext,
                Exception = self._exception
            });
        }
    }

    public class LogContextHttpRequestMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public IHttpServerStagedContext HttpContext { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextHttp.Request"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("HTTP request {0} arrived", HttpContext.Url); }
        }
    }

    public class LogContextHttpResponseMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public IHttpServerStagedContext HttpContext { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextHttp.Response"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "HTTP response {0} dispatched ({1} ms)",
                    HttpContext.StatusCode, DurationMs);
            }
        }
    }

    public class LogContextHttpCrashedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public IHttpServerStagedContext HttpContext { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextHttp.Crashed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Error; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "HTTP request {0} caused crash: {1}",
                    HttpContext.Url, Exception);
            }
        }
    }

    public class LogContextCommand
    {
        private ILogContextAdvanced _context;
        private Stopwatch _stopwatch;
        private object _command;
        private CommandResult _result;
        private Exception _exception;

        private LogContextCommand() { }

        public object Command { get { return _command; } }
        public CommandResult Result { get { return _result; } }
        public Exception Exception { get { return _exception; } }

        public static void Arrived(ILogContext context, object command)
        {
            var self = new LogContextCommand();
            self._command = command;
            self._context = context.Advanced;
            self._stopwatch = new Stopwatch();
            self._stopwatch.Start();
            self._context.SetContext(self);
            self._context.Log(new LogContextCommandArrivedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Command = command
            });
        }
        public static void ProducedEvent(ILogContext context, object evnt)
        {
            var self = context.Advanced.GetContext<LogContextCommand>();
            self._context.Log(new LogContextCommandProducedEventMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Command = self._command,
                Event = evnt
            });
        }
        public static void Finished(ILogContext context, CommandResult result)
        {
            var self = context.Advanced.GetContext<LogContextCommand>();
            self._result = result;
            self._stopwatch.Stop();
            self._context.Log(new LogContextCommandFinishedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Command = self._command,
                Result = self._result,
                DurationMs = self._stopwatch.ElapsedMilliseconds
            });
        }
        public static void Failed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextCommand>();
            self._exception = exception;
            self._stopwatch.Stop();
            self._context.Log(new LogContextCommandFailedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Command = self._command,
                Exception = exception
            });
        }
        public static void Crashed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextCommand>();
            self._exception = exception;
            self._stopwatch.Stop();
            self._context.Log(new LogContextCommandCrashedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Command = self._command,
                Exception = exception
            });
        }
    }

    public class LogContextCommandArrivedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Command { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextCommand.Arrived"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Command {0} arrived", Command.GetType().FullName); }
        }
    }

    public class LogContextCommandCrashedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Command { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextCommand.Crashed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Error; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Command {0} crashed: {1}", Command.GetType().FullName, Exception); }
        }
    }

    public class LogContextCommandFailedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Command { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextCommand.Crashed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Transient; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Command {0} could not be processed and should be retried: {1}",
                    Command.GetType().FullName, Exception);
            }
        }
    }

    public class LogContextCommandFinishedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Command { get; set; }
        public CommandResult Result { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextCommand.Finished"; } }
        LogContextLevel ILogContextMessage.Level
        {
            get
            {
                return (Result.Status == CommandResultStatus.InternalError)
                    ? LogContextLevel.Error : LogContextLevel.Information;
            }
        }
        string ILogContextMessage.Message
        {
            get
            {
                if (Result.Status == CommandResultStatus.Success)
                {
                    return string.Format(
                        "Command {0} processed (tracking id {1})",
                           Command.GetType().FullName, Result.TrackingId);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendFormat("Command {0} failed: ", Command.GetType().FullName);
                    bool first = true;
                    foreach (var error in Result.Errors)
                    {
                        if (!first)
                            sb.Append(",");
                        first = false;
                        sb.Append(error.Message);
                    }
                    return sb.ToString();
                }
            }
        }
    }

    public class LogContextCommandProducedEventMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Command { get; set; }
        public object Event { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextCommand.ProducedEvent"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format(""); }
        }
    }

    public class LogContextQuery
    {
        private ILogContextAdvanced _context;
        private object _query, _response;
        private Stopwatch _stopwatch;
        private Exception _exception;

        private LogContextQuery() { }

        public object Query { get { return _query; } }
        public object Response { get { return _response; } }
        public Exception Exception { get { return _exception; } }

        public static void WrongRequest(ILogContext context, object query, Exception exception)
        {
            var self = new LogContextQuery();
            self._context = context.Advanced;
            self._query = query;
            self._exception = exception;
            self._stopwatch = new Stopwatch();
            self._context.SetContext(self);
            self._context.Log(new LogContextQueryWrongMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Query = self._query,
                Exception = exception
            });
        }

        public static void Arrived(ILogContext context, object query)
        {
            var self = new LogContextQuery();
            self._context = context.Advanced;
            self._query = query;
            self._stopwatch = new Stopwatch();
            self._stopwatch.Start();
            self._context.SetContext(self);
            self._context.Log(new LogContextQueryArrivedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Query = self._query
            });
        }

        public static void Finished(ILogContext context, object response)
        {
            var self = context.Advanced.GetContext<LogContextQuery>();
            self._response = response;
            self._stopwatch.Stop();
            self._context.Log(new LogContextQueryFinishedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Query = self._query,
                Response = self._response,
                DurationMs = self._stopwatch.ElapsedMilliseconds
            });
        }

        public static void Failed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextQuery>();
            self._exception = exception;
            self._stopwatch.Stop();
            self._context.Log(new LogContextQueryFailedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Query = self._query,
                Exception = self._exception,
                DurationMs = self._stopwatch.ElapsedMilliseconds
            });
        }

        public static void Crashed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextQuery>();
            self._exception = exception;
            self._stopwatch.Stop();
            self._context.Log(new LogContextQueryCrashedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                Query = self._query,
                Exception = self._exception,
                DurationMs = self._stopwatch.ElapsedMilliseconds
            });
        }
    }

    public class LogContextQueryArrivedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Query { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextQuery.Arrived"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Query request {0} arrived", Query.GetType().FullName); }
        }
    }

    public class LogContextQueryFinishedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Query { get; set; }
        public object Response { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextQuery.Finished"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Query response {0} dispatched", Response.GetType().FullName); }
        }
    }

    public class LogContextQueryWrongMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Query { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextQuery.WrongRequest"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Wrong query request {0} arrived: {1}",
                    Query.GetType().FullName, Exception.Message);
            }
        }
    }

    public class LogContextQueryFailedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Query { get; set; }
        public Exception Exception { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextQuery.Failed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Transient; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Query request {0} could not be fulfilled: {1}",
                    Query.GetType().FullName, Exception.Message);
            }
        }
    }

    public class LogContextQueryCrashedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public object Query { get; set; }
        public Exception Exception { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextQuery.Crashed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Error; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Error occurred while processing query request {0}: {1}",
                    Query.GetType().FullName, Exception.Message);
            }
        }
    }



    public class LogContextEvent
    {
        private ILogContextAdvanced _context;
        private string _projectionName;
        private EventProjectionUpgradeMode _upgradeMode;
        private EventStoreToken _currentToken;
        private Stopwatch _flushStopwatch, _eventStopwatch, _rebuildStopwatch;
        private int _flushEventsCount, _rebuildEventsCount;
        private bool _flushActive;
        private EventStoreEvent _processedEvent;
        private object _deserializedEvent;

        private LogContextEvent() { }

        public string ProjectionName { get { return _projectionName; } }
        public EventProjectionUpgradeMode UpgradeMode { get { return _upgradeMode; } }
        public EventStoreToken CurrentToken { get { return _currentToken; } }
        public EventStoreEvent CurrentEvent { get { return _processedEvent; } }
        public object DeserializedEvent { get { return _deserializedEvent; } }

        public static void Initialization(
            ILogContext context, string projectionName,
            EventProjectionUpgradeMode upgradeMode, EventStoreToken startingToken)
        {
            var self = new LogContextEvent();
            self._context = context.Advanced;
            self._projectionName = projectionName;
            self._upgradeMode = upgradeMode;
            self._currentToken = startingToken;
            self._flushStopwatch = new Stopwatch();
            self._rebuildStopwatch = new Stopwatch();
            if (upgradeMode == EventProjectionUpgradeMode.Rebuild)
                self._rebuildStopwatch.Start();
            self._context.SetContext(self);
            self._context.Log(new LogContextEventInitializedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = projectionName,
                UpgradeMode = upgradeMode,
                StartingToken = startingToken
            });
        }

        public static void RebuildFinished(ILogContext context)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._rebuildStopwatch.Stop();
            self._context.Log(new LogContextEventRebuildFinishedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                ProcessedEventsCount = self._rebuildEventsCount,
                ProcessedEventsDurationMs = self._rebuildStopwatch.ElapsedMilliseconds
            });
        }

        public static void Flushed(ILogContext context)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._flushStopwatch.Stop();
            self._context.Log(new LogContextEventFlushedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                ProcessedEventsCount = self._flushEventsCount,
                ProcessedEventsDurationMs = self._flushStopwatch.ElapsedMilliseconds
            });
            self._flushEventsCount = 0;
            self._flushActive = false;
            self._deserializedEvent = null;
            self._processedEvent = null;
        }

        public static void EventArrived(ILogContext context, EventStoreEvent storedEvent, object deserializedEvent)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._eventStopwatch.Start();
            self._processedEvent = storedEvent;
            self._deserializedEvent = deserializedEvent;
            self._currentToken = storedEvent.Token;
            self._context.Log(new LogContextEventArrivedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                SerializedEvent = storedEvent,
                DeserializedEvent = self._deserializedEvent
            });
        }

        public static void EventProcessed(ILogContext context)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._flushActive = true;
            self._eventStopwatch.Stop();
            self._flushEventsCount++;
            self._rebuildEventsCount++;
            self._context.Log(new LogContextEventProcessedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                SerializedEvent = self._processedEvent,
                DeserializedEvent = self._deserializedEvent,
                DurationMs = self._eventStopwatch.ElapsedMilliseconds
            });
        }

        public static void EventFailed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._eventStopwatch.Stop();
            self._context.Log(new LogContextEventFailedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                SerializedEvent = self._processedEvent,
                DeserializedEvent = self._deserializedEvent,
                DurationMs = self._eventStopwatch.ElapsedMilliseconds,
                Exception = exception
            });
        }

        public static void EventCrashed(ILogContext context, Exception exception)
        {
            var self = context.Advanced.GetContext<LogContextEvent>();
            self._eventStopwatch.Stop();
            self._context.Log(new LogContextEventCrashedMessage
            {
                Timestamp = self._context.CurrentTimestamp(),
                ProjectionName = self._projectionName,
                SerializedEvent = self._processedEvent,
                DeserializedEvent = self._deserializedEvent,
                DurationMs = self._eventStopwatch.ElapsedMilliseconds,
                Exception = exception
            });
        }
    }

    public class LogContextMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string Origin { get; set; }
        public LogContextLevel Level { get; set; }
        public string Message { get; set; }
    }

    public class LogContextEventInitializedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public EventProjectionUpgradeMode UpgradeMode { get; set; }
        public EventStoreToken StartingToken { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.Initialized"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Information; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Projection {0} initialized ({1} at {2})",
                    ProjectionName, UpgradeMode, StartingToken);
            }
        }
    }

    public class LogContextEventFlushedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public long FlushDurationMs { get; set; }
        public long ProcessedEventsDurationMs { get; set; }
        public long ProcessedEventsCount { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.Flushed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Projection {0} flushed it's data to storage", ProjectionName); }
        }
    }

    public class LogContextEventRebuildFinishedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public long ProcessedEventsDurationMs { get; set; }
        public long ProcessedEventsCount { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.RebuildFinished"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get { return string.Format("Rebuild of projection {0} finished", ProjectionName); }
        }
    }

    public class LogContextEventArrivedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public EventStoreEvent SerializedEvent { get; set; }
        public object DeserializedEvent { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.EventArrived"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format(
                    "Starting processing of event {0} (token {1}) in projection {1}",
                    SerializedEvent.Type, SerializedEvent.Token, ProjectionName);
            }
        }
    }

    public class LogContextEventProcessedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public EventStoreEvent SerializedEvent { get; set; }
        public object DeserializedEvent { get; set; }
        public long DurationMs { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.EventProcessed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Debug; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format("Event {0} successfully processed in projection {1}",
                    SerializedEvent.Type, ProjectionName);
            }
        }
    }

    public class LogContextEventFailedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public EventStoreEvent SerializedEvent { get; set; }
        public object DeserializedEvent { get; set; }
        public long DurationMs { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.EventFailed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Transient; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format("Event {0} in projection {1} failed and should be retried. Error message: {2}",
                    SerializedEvent.Type, ProjectionName, Exception.Message);
            }
        }
    }

    public class LogContextEventCrashedMessage : ILogContextMessage
    {
        public DateTime Timestamp { get; set; }
        public string ProjectionName { get; set; }
        public EventStoreEvent SerializedEvent { get; set; }
        public object DeserializedEvent { get; set; }
        public long DurationMs { get; set; }
        public Exception Exception { get; set; }

        string ILogContextMessage.Origin { get { return "ServiceLib.LogContextEvent.EventCrashed"; } }
        LogContextLevel ILogContextMessage.Level { get { return LogContextLevel.Error; } }
        string ILogContextMessage.Message
        {
            get
            {
                return string.Format("Event {0} in projection {1} crashed. {2}",
                    SerializedEvent.Type, ProjectionName, Exception);
            }
        }
    }
}
