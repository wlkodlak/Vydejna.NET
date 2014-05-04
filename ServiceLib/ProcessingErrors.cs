using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class ValidationErrorException : Exception
    {
        public string Field { get; private set; }
        public string Category { get; private set; }
        private string _message;
        public override string Message { get { return _message; } }

        public ValidationErrorException(string field, string category, string message)
        {
            Field = field;
            Category = category;
            _message = message;
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
        public int Retries { get; private set; }
        public string Category { get; private set; }
        public TransientErrorException(string category, Exception cause, int retries = -1)
            : base(GenerateMessage(category, cause, retries), cause)
        {
            Category = category;
            Retries = retries;
        }
        public TransientErrorException(string category, string cause, int retries = -1)
            : base(GenerateMessage(category, cause, retries))
        {
            Category = category;
            Retries = retries;
        }
        private static string GenerateMessage(string category, Exception cause, int retries)
        {
            return GenerateMessage(category, cause.Message, retries);
        }
        private static string GenerateMessage(string category, string cause, int retries)
        {
            return string.Concat("Transient error (category ", category, ", ", retries < 0 ? "unknown" : retries.ToString(), " retries): ", cause);
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
