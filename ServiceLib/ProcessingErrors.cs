using System;
using System.Collections.Generic;

namespace ServiceLib
{
    public enum CommandResultStatus
    {
        Success,
        InvalidCommand,
        WrongState,
        InternalError
    }

    public class CommandResult
    {
        private static readonly CommandError[] _emptyErrors;

        static CommandResult()
        {
            _emptyErrors = new CommandError[0];
        }

        public CommandResultStatus Status { get; private set; }
        public IList<CommandError> Errors { get; private set; }
        public string TrackingId { get; private set; }

        public CommandResult(CommandResultStatus status, IList<CommandError> errors, string trackingId)
        {
            Status = status;
            Errors = errors;
            TrackingId = trackingId;
        }

        public static CommandResult Success(string trackingId)
        {
            return new CommandResult(CommandResultStatus.Success, _emptyErrors, trackingId);
        }

        public static CommandResult From(IList<CommandError> errors)
        {
            if (errors == null || errors.Count == 0)
                return new CommandResult(CommandResultStatus.Success, _emptyErrors, null);
            else
                return new CommandResult(CommandResultStatus.InvalidCommand, errors, null);
        }

        public static CommandResult From(CommandError error)
        {
            if (error == null)
                throw new ArgumentNullException("error");
            return new CommandResult(CommandResultStatus.InvalidCommand, new[] {error}, null);
        }

        public static CommandResult From(DomainErrorException error)
        {
            if (error == null)
                throw new ArgumentNullException("error");
            return new CommandResult(CommandResultStatus.WrongState, new[] {new CommandError(error)}, null);
        }

        public static CommandResult From(TransientErrorException error)
        {
            return From(new CommandError("__TRANSIENT__", error.Category, error.Cause));
        }

        public static CommandResult From(Exception exception)
        {
            var error = new CommandError("__SYSTEM__", exception.GetType().Name, ExceptionMessage(exception));
            return new CommandResult(CommandResultStatus.InternalError, new[] {error}, null);
        }

        private static string ExceptionMessage(Exception exception)
        {
#if DEBUG
            return exception.ToString();
#else
            return exception.Message;
#endif
        }
    }

    public class CommandError
    {
        public string Field { get; private set; }
        public string Category { get; private set; }
        public string Message { get; private set; }

        public CommandError(string field, string category, string message)
        {
            Field = field;
            Category = category;
            Message = message;
        }

        public CommandError(DomainErrorException error)
        {
            Field = error.Field;
            Category = error.Category;
            Message = error.Message;
        }
    }

    public class DomainErrorException : Exception
    {
        public string Field { get; private set; }
        public string Category { get; private set; }
        private readonly string _message;

        public override string Message
        {
            get { return _message; }
        }

        public DomainErrorException(string field, string category, string message)
        {
            Field = field;
            Category = category;
            _message = message;
        }
    }

    public class TransientErrorException : Exception
    {
        public string Category { get; private set; }
        public string Cause { get; private set; }

        public TransientErrorException(string category, Exception cause)
            : base(GenerateMessage(category, cause), cause)
        {
            Category = category;
            Cause = cause.Message;
        }

        public TransientErrorException(string category, string cause)
            : base(GenerateMessage(category, cause))
        {
            Category = category;
            Cause = cause;
        }

        private static string GenerateMessage(string category, Exception cause)
        {
            return GenerateMessage(category, cause.Message);
        }

        private static string GenerateMessage(string category, string cause)
        {
            return string.Concat("Transient error (category ", category, "): ", cause);
        }
    }

    public static class ProcessingErrorsExtensions
    {
        public static void Throw(this Exception exception)
        {
            if (exception != null)
                throw exception;
        }
    }
}