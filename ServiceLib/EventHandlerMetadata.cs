using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private string _baseName;
        private string _versionDoc;
        private string _tokenDoc;
        private IDocumentFolder _store;
        private int _versionVer;
        private int _tokenVer;
        private static readonly MetadataTraceSource Logger = new MetadataTraceSource("ServiceLib.EventHandlerMetadata");

        public MetadataInstance(string baseName, string versionDoc, string tokenDoc, IDocumentFolder store)
        {
            this._baseName = baseName;
            this._versionDoc = versionDoc;
            this._tokenDoc = tokenDoc;
            this._versionVer = 0;
            this._tokenVer = 0;
            this._store = store;
        }

        public string ProcessName
        {
            get { return _baseName; }
        }

        public Task<EventStoreToken> GetToken()
        {
            return _store.GetDocument(_tokenDoc).ContinueWith(task =>
            {
                var doc = task.Result;
                EventStoreToken token;
                if (doc == null)
                {
                    _tokenVer = 0;
                    token = EventStoreToken.Initial;
                }
                else
                {
                    _tokenVer = doc.Version;
                    token = new EventStoreToken(doc.Contents);
                }
                Logger.TokenLoaded(_tokenVer, token);
                return token;
            });
        }

        public Task SetToken(EventStoreToken token)
        {
            return _store.SaveDocument(_tokenDoc, token.ToString(), DocumentStoreVersion.At(_tokenVer), null).ContinueWith(task =>
            {
                if (!task.Result)
                {
                    Logger.TokenSavingFailedDueToConcurrency(_tokenDoc, _tokenVer, token);
                    throw new MetadataInstanceConcurrencyException().WithDetails(_versionDoc, _versionVer, token);
                }
                _tokenVer++;
                Logger.TokenSavedSuccessfully(_tokenVer, token);
            });
        }

        public Task<string> GetVersion()
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return TaskUtils.FromResult<string>(null);
            return _store.GetDocument(_versionDoc).ContinueWith(task =>
            {
                var doc = task.Result;
                string version;
                if (doc == null)
                {
                    _versionVer = 0;
                    version = null;
                }
                else
                {
                    _versionVer = doc.Version;
                    version = doc.Contents;
                }
                Logger.VersionLoaded(_versionVer, version);
                return version;
            });
        }

        public Task SetVersion(string version)
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return TaskUtils.FromResult<object>(null);
            return _store.SaveDocument(_versionDoc, version, DocumentStoreVersion.At(_versionVer), null).ContinueWith(task =>
            {
                if (!task.Result)
                {
                    Logger.VersionSavingFailedDueToConcurrency(_versionDoc, _versionVer, version);
                    throw new MetadataInstanceConcurrencyException().WithDetails(_versionDoc, _versionVer, version);
                }
                _versionVer++;
                Logger.VersionSaved(_versionVer, version);
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
            : base(info, context)
        {
        }

        public MetadataInstanceConcurrencyException WithDetails(string documentName, int expectedVersion, object dataToSave)
        {
            Data["DocumentName"] = documentName;
            Data["ExpectedVersion"] = expectedVersion;
            Data["DataToSave"] = dataToSave.ToString();
            return this;
        }
    }

    public class MetadataTraceSource : TraceSource
    {
        public MetadataTraceSource(string name)
            : base(name)
        {
        }

        public void TokenLoaded(int documentVersion, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Loaded token {Token}");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void TokenSavingFailedDueToConcurrency(string documentName, int documentVersion, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Error, 3, "Token {Token} could not be saved due to concurrency");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void TokenSavedSuccessfully(int documentVersion, EventStoreToken token)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 2, "Token {Token} saved");
            msg.SetProperty("Token", false, token);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void VersionLoaded(int documentVersion, string version)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 6, "Loaded version {Version}");
            msg.SetProperty("Version", false, version);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void VersionSavingFailedDueToConcurrency(string documentName, int documentVersion, string version)
        {
            var msg = new LogContextMessage(TraceEventType.Error, 8, "Version {Version} could not be saved due to concurrency");
            msg.SetProperty("Version", false, version);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void VersionSaved(int documentVersion, string version)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 7, "Version {Version} saved");
            msg.SetProperty("Version", false, version);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }
    }
}
