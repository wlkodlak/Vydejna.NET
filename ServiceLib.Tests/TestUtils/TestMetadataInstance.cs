using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestMetadataInstance : IMetadataInstance
    {
        public EventStoreToken Token;
        public string Version;
        public bool FailMode;
        public string ProcessName { get; set; }

        public TestMetadataInstance()
        {
            Token = EventStoreToken.Initial;
            Version = "";
        }

        public Task<EventStoreToken> GetToken()
        {
            if (FailMode)
                return TaskUtils.FromError<EventStoreToken>(new SystemException("Metadata failing"));
            else
                return TaskUtils.FromResult(Token);
        }

        public Task SetToken(EventStoreToken token)
        {
            if (FailMode)
                return TaskUtils.FromError<object>(new SystemException("Metadata failing"));
            else
            {
                Token = token;
                return TaskUtils.CompletedTask();
            }
        }

        public Task<string> GetVersion()
        {
            if (FailMode)
                return TaskUtils.FromError<string>(new SystemException("Metadata failing"));
            else
                return TaskUtils.FromResult(Version);
        }

        public Task SetVersion(string version)
        {
            if (FailMode)
                return TaskUtils.FromError<object>(new SystemException("Metadata failing"));
            else
            {
                Version = version;
                return TaskUtils.CompletedTask();
            }
        }
    }
}
