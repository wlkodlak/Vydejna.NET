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
        void GetToken(Action<EventStoreToken> onCompleted, Action<Exception> onError);
        void SetToken(EventStoreToken token, Action onCompleted, Action<Exception> onError);
        void GetVersion(Action<string> onCompleted, Action<Exception> onError);
        void SetVersion(string version, Action onCompleted, Action<Exception> onError);
        IDisposable Lock(Action onLockObtained);
        void Unlock();
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
            return new MetadataInstance(consumerName, consumerName + "_ver", consumerName + "_tok", _store, _locking);
        }
    }

    public class MetadataInstance : IMetadataInstance
    {
        private string _lockName;
        private string _versionDoc;
        private string _tokenDoc;
        private IDocumentFolder _store;
        private INodeLockManager _locking;

        public MetadataInstance(string lockName, string versionDoc, string tokenDoc, IDocumentFolder store, INodeLockManager locking)
        {
            this._lockName = lockName;
            this._versionDoc = versionDoc;
            this._tokenDoc = tokenDoc;
            this._store = store;
            this._locking = locking;
        }

        public void GetToken(Action<EventStoreToken> onCompleted, Action<Exception> onError)
        {
            _store.GetDocument(_tokenDoc, (v, c) => onCompleted(new EventStoreToken(c)), () => onCompleted(EventStoreToken.Initial), onError);
        }

        public void SetToken(EventStoreToken token, Action onCompleted, Action<Exception> onError)
        {
            _store.SaveDocument(_tokenDoc, token.ToString(), DocumentStoreVersion.Any, onCompleted, onCompleted, onError);
        }

        public void GetVersion(Action<string> onCompleted, Action<Exception> onError)
        {
            if (string.IsNullOrEmpty(_versionDoc))
                onCompleted(null);
            else
                _store.GetDocument(_versionDoc, (v, c) => onCompleted(c), () => onCompleted(null), onError);
        }

        public void SetVersion(string version, Action onCompleted, Action<Exception> onError)
        {
            if (string.IsNullOrEmpty(_versionDoc))
                onCompleted();
            else
                _store.SaveDocument(_versionDoc, version, DocumentStoreVersion.Any, onCompleted, onCompleted, onError);
        }

        public IDisposable Lock(Action onLockObtained)
        {
            return _locking.Lock(_lockName, onLockObtained, () => { }, false);
        }

        public void Unlock()
        {
            _locking.Unlock(_lockName);
        }
    }
}
