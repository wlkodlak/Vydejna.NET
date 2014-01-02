using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ServiceLib.Tests.Http
{
    [TestClass]
    public class HttpStagedParametersTests
    {
        /*
         * Enumerates parameter added using AddParameter
         * Get returns IProcessedParameter that has all the values
         */
    }
    [TestClass]
    public class HttpProcessedParameterTests
    {
        /*
         * Successful conversion creates parsed typed parameter
         * Missing parameter creates empty typed parameter
         * Failing conversion immediatelly throws
         */
    }
    [TestClass]
    public class HttpTypedProcessedParameterTests
    {
        /*
         * Get actual value
         * Get default value of type
         * Get manual default value
         * Throw on get for missing mandatory parameter
         * Do not validate empty values
         * Validation may throw for invalid parameters
         */
    }
}
