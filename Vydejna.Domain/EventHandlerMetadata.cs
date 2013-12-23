using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface IMetadataManager
    {
        IMetadataInstance GetConsumer(string consumerName);
    }

    public interface IMetadataInstance
    {
        void GetData(Action<MetadataInfo> onCompleted, Action<Exception> onError);
        void SetData(MetadataInfo info, Action onCompleted, Action<Exception> onError);
        void Lock(Action onLockObtained);
        void CancelLock();
    }

    public class MetadataInfo
    {
        private readonly EventStoreToken _token;
        private readonly string _version;
        private readonly string _serialized;

        public MetadataInfo(EventStoreToken token, string version)
            : this(token, version, Serialize(token, version))
        {
        }
        private MetadataInfo(EventStoreToken token, string version, string serialized)
        {
            _token = token;
            _version = version;
            _serialized = serialized;
        }

        public EventStoreToken Token { get { return _token; } }
        public string Version { get { return _version; } }
        public string Serialize() { return _serialized; }

        private static string Serialize(EventStoreToken token, string version)
        {
            return string.Format("{0}\r\n{1}", token.ToString(), version);
        }
        public static MetadataInfo Deserialize(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
                return new MetadataInfo(EventStoreToken.Initial, "");
            var parts = serialized.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var token = new EventStoreToken(parts[0]);
            var version = parts.Length >= 2 ? parts[1] : "";
            return new MetadataInfo(token, version, serialized);
        }
    }

    public class MetadataManager : IMetadataManager
    {
        private IDocumentFolder _store;
        private INodeLockManager _locking;

        public MetadataManager(IDocumentFolder store, INodeLockManager locking)
        {
            _store = store;
            _locking = locking;
        }

        public IMetadataInstance GetConsumer(string consumerName)
        {
            return new MetadataInstance(this, consumerName);
        }

        private class MetadataInstance : IMetadataInstance
        {
            private MetadataManager _parent;
            private string _name;
            private LockExecutor _lockExecutor;

            public MetadataInstance(MetadataManager parent, string name)
            {
                _parent = parent;
                _name = name;
            }

            public void GetData(Action<MetadataInfo> onCompleted, Action<Exception> onError)
            {
                _parent._store.GetDocument(_name,
                    (v, c) => onCompleted(MetadataInfo.Deserialize(c)),
                    () => onCompleted(MetadataInfo.Deserialize(null)),
                    onError);
            }

            public void SetData(MetadataInfo info, Action onCompleted, Action<Exception> onError)
            {
                _parent._store.SaveDocument(_name, info.Serialize(), DocumentStoreVersion.Any, onCompleted, () => { }, onError);
            }

            public void Lock(Action onLockObtained)
            {
                _lockExecutor = new LockExecutor(_parent._locking, _name, onLockObtained);
                _lockExecutor.Execute();
            }

            private class LockExecutor
            {
                private INodeLockManager _locking;
                private string _name;
                private Action _onLockObtained;
                private bool _lockCanceled;
                private bool _isLocked;

                public LockExecutor(INodeLockManager locking, string name, Action onLockObtained)
                {
                    _locking = locking;
                    _name = name;
                    _onLockObtained = onLockObtained;
                    _lockCanceled = false;
                }
                public void Execute()
                {
                    _locking.Lock(_name, OnLockCompleted);
                }
                private void OnLockCompleted(bool obtained)
                {
                    if (obtained)
                    {
                        _isLocked = true;
                        _onLockObtained();
                    }
                    else
                        _locking.WaitForLock(_name, OnLockAvailable);
                }
                private void OnLockAvailable()
                {
                    if (!_lockCanceled)
                        _locking.Lock(_name, OnLockCompleted);
                }
                public void CancelLock()
                {
                    _lockCanceled = true;
                    if (_isLocked)
                    {
                        _locking.Unlock(_name);
                        _isLocked = false;
                    }
                }
            }

            public void CancelLock()
            {
                if (_lockExecutor != null)
                    _lockExecutor.CancelLock();
            }
        }
    }
}
