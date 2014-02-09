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

    public static class ProcessingErrorsExtensions
    {
        public static void Throw(this Exception exception)
        {
            if (exception != null)
                throw exception;
        }
    }
}
