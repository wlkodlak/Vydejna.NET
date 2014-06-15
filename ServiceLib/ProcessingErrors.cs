using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private static readonly CommandError[] _emptyErrors = new CommandError[0];
        private static readonly CommandResult _successResult;
        private static readonly Task<CommandResult> _successTask;

        static CommandResult()
        {
            _emptyErrors = new CommandError[0];
            _successResult = new CommandResult(CommandResultStatus.Success, _emptyErrors);
            var tcsSuccess = new TaskCompletionSource<CommandResult>();
            tcsSuccess.TrySetResult(_successResult);
            _successTask = tcsSuccess.Task;
        }

        public CommandResultStatus Status { get; set; }
        public IList<CommandError> Errors { get; set; }

        public CommandResult() { }

        public CommandResult(CommandResultStatus status, IList<CommandError> errors)
        {
            Status = status;
            Errors = errors;
        }

        public static CommandResult Ok { get { return _successResult; } }
        public static Task<CommandResult> TaskOk { get { return _successTask; } }

        public static CommandResult From(IList<CommandError> errors)
        {
            if (errors == null || errors.Count == 0)
                return _successResult;
            else
                return new CommandResult(CommandResultStatus.InvalidCommand, errors);
        }

        public static CommandResult From(CommandError error)
        {
            if (error == null)
                throw new ArgumentNullException("error");
            return new CommandResult(CommandResultStatus.InvalidCommand, new[] { error });
        }

        public static CommandResult From(DomainErrorException error)
        {
            if (error == null)
                throw new ArgumentNullException("error");
            return new CommandResult(CommandResultStatus.WrongState, new[] { new CommandError(error) });
        }

        public static CommandResult From(Exception exception)
        {
            var error = new CommandError("__SYSTEM__", exception.GetType().Name, ExceptionMessage(exception));
            return new CommandResult(CommandResultStatus.InternalError, new[] { error });
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
        private string _message;
        public override string Message { get { return _message; } }

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
        public TransientErrorException(string category, Exception cause)
            : base(GenerateMessage(category, cause), cause)
        {
            Category = category;
        }
        public TransientErrorException(string category, string cause)
            : base(GenerateMessage(category, cause))
        {
            Category = category;
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
