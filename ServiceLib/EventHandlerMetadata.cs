using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IMetadataManager
    {
        IMetadataInstance GetConsumer(string consumerName);
    }

    public interface IMetadataInstance
    {
        string ProcessName { get; }
        Task<EventStoreToken> GetToken();
        Task SetToken(EventStoreToken token);
        Task<string> GetVersion();
        Task SetVersion(string version);
    }

    public class MetadataManager : IMetadataManager
    {
        private IDocumentFolder _store;

        public MetadataManager(IDocumentFolder store)
        {
            _store = store;
        }

        public IMetadataInstance GetConsumer(string consumerName)
        {
            return new MetadataInstance(consumerName, consumerName + "_ver", consumerName + "_tok", _store);
        }
    }

    public class MetadataInstance : IMetadataInstance
    {
        private string _lockName;
        private string _versionDoc;
        private string _tokenDoc;
        private IDocumentFolder _store;
        private int _versionVer;
        private int _tokenVer;

        public MetadataInstance(string lockName, string versionDoc, string tokenDoc, IDocumentFolder store)
        {
            this._lockName = lockName;
            this._versionDoc = versionDoc;
            this._tokenDoc = tokenDoc;
            this._versionVer = 0;
            this._tokenVer = 0;
            this._store = store;
        }

        public string ProcessName
        {
            get { return _lockName; }
        }

        public Task<EventStoreToken> GetToken()
        {
            return _store.GetDocument(_tokenDoc).ContinueWith(task =>
            {
                var doc = task.Result;
                if (doc == null)
                {
                    _tokenVer = 0;
                    return EventStoreToken.Initial;
                }
                else
                {
                    _tokenVer = doc.Version;
                    return new EventStoreToken(doc.Contents);
                }
            });
        }

        public Task SetToken(EventStoreToken token)
        {
            return _store.SaveDocument(_tokenDoc, token.ToString(), DocumentStoreVersion.At(_tokenVer), null).ContinueWith(task =>
            {
                if (!task.Result)
                    throw new MetadataInstanceConcurrencyException();
            });
        }

        public Task<string> GetVersion()
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return TaskUtils.FromResult<string>(null);
            return _store.GetDocument(_versionDoc).ContinueWith(task =>
            {
                var doc = task.Result;
                if (doc == null)
                {
                    _versionVer = 0;
                    return null;
                }
                else
                {
                    _versionVer = doc.Version;
                    return doc.Contents;
                }
            });
        }

        public Task SetVersion(string version)
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return TaskUtils.FromResult<object>(null);
            return _store.SaveDocument(_versionDoc, version, DocumentStoreVersion.At(_versionVer), null).ContinueWith(task =>
            {
                if (!task.Result)
                    throw new MetadataInstanceConcurrencyException();
            });
        }
    }

    [Serializable]
    public class MetadataInstanceConcurrencyException : Exception
    {
        public MetadataInstanceConcurrencyException() { }
        public MetadataInstanceConcurrencyException(string message) : base(message) { }
        public MetadataInstanceConcurrencyException(string message, Exception inner) : base(message, inner) { }
        protected MetadataInstanceConcurrencyException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }
    }
}
