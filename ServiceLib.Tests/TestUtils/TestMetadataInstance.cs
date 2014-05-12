using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestMetadataInstance : IMetadataInstance
    {
        public EventStoreToken Token;
        public string Version;
        public bool WaitsForLock;
        public bool IsLocked;
        public bool FailMode;
        private TaskCompletionSource<object> _taskLock;
        private CancellationTokenRegistration _taskCancel;
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

        public void SendLock()
        {
            if (WaitsForLock)
            {
                IsLocked = true;
                WaitsForLock = false;
                _taskCancel.Dispose();
                _taskLock.TrySetResult(null);
            }
        }

        public Task Lock(CancellationToken cancel)
        {
            WaitsForLock = true;
            _taskLock = new TaskCompletionSource<object>();
            _taskCancel = cancel.Register(() => _taskLock.TrySetCanceled());
            return _taskLock.Task;
        }

        public void Unlock()
        {
            IsLocked = false;
        }
    }
}
