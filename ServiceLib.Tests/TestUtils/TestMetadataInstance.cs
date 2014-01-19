using System;

namespace ServiceLib.Tests.TestUtils
{
    public class TestMetadataInstance : IMetadataInstance
    {
        public EventStoreToken Token;
        public string Version;
        public bool WaitsForLock;
        public bool IsLocked;
        public bool FailMode;
        public string ProcessName { get; set; }
        private Action _onLockObtained;

        public TestMetadataInstance()
        {
            Token = EventStoreToken.Initial;
            Version = "";
        }

        private class WaitForLock : IDisposable
        {
            public TestMetadataInstance TestMetadata;
            public void Dispose()
            {
                TestMetadata.WaitsForLock = false;
            }
        }

        public void GetToken(Action<EventStoreToken> onCompleted, Action<Exception> onError)
        {
            if (FailMode)
                onError(new SystemException("Metadata failing"));
            else
                onCompleted(Token);
        }

        public void SetToken(EventStoreToken token, Action onCompleted, Action<Exception> onError)
        {
            if (FailMode)
                onError(new SystemException("Metadata failing"));
            else
            {
                Token = token;
                onCompleted();
            }
        }

        public void GetVersion(Action<string> onCompleted, Action<Exception> onError)
        {
            if (FailMode)
                onError(new SystemException("Metadata failing"));
            else
                onCompleted(Version);
        }

        public void SetVersion(string version, Action onCompleted, Action<Exception> onError)
        {
            if (FailMode)
                onError(new SystemException("Metadata failing"));
            else
            {
                Version = version;
                onCompleted();
            }
        }

        public void SendLock()
        {
            if (WaitsForLock)
            {
                IsLocked = true;
                WaitsForLock = false;
                _onLockObtained();
            }
        }

        public IDisposable Lock(Action onLockObtained)
        {
            _onLockObtained = onLockObtained;
            WaitsForLock = true;
            var waiter = new WaitForLock { TestMetadata = this };
            return waiter;
        }

        public void Unlock()
        {
            IsLocked = false;
        }
    }
}
