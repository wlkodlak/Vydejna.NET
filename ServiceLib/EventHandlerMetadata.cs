using System;
using System.Diagnostics;
using System.Runtime.Serialization;
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
        private readonly IDocumentFolder _store;

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
        private readonly string _baseName;
        private readonly string _versionDoc;
        private readonly string _tokenDoc;
        private readonly IDocumentFolder _store;
        private int _versionVer;
        private int _tokenVer;
        private static readonly MetadataTraceSource Logger = new MetadataTraceSource("ServiceLib.EventHandlerMetadata");

        public MetadataInstance(string baseName, string versionDoc, string tokenDoc, IDocumentFolder store)
        {
            _baseName = baseName;
            _versionDoc = versionDoc;
            _tokenDoc = tokenDoc;
            _versionVer = 0;
            _tokenVer = 0;
            _store = store;
        }

        public string ProcessName
        {
            get { return _baseName; }
        }

        public async Task<EventStoreToken> GetToken()
        {
            var doc = await _store.GetDocument(_tokenDoc);
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
        }

        public async Task SetToken(EventStoreToken token)
        {
            var saved = await _store.SaveDocument(_tokenDoc, token.ToString(), DocumentStoreVersion.At(_tokenVer), null);
            if (!saved)
            {
                Logger.TokenSavingFailedDueToConcurrency(_tokenDoc, _tokenVer, token);
                throw new MetadataInstanceConcurrencyException(_versionDoc, _versionVer, token);
            }
            _tokenVer++;
            Logger.TokenSavedSuccessfully(_tokenVer, token);
        }

        public async Task<string> GetVersion()
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return null;

            var doc = await _store.GetDocument(_versionDoc);
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
        }

        public async Task SetVersion(string version)
        {
            if (string.IsNullOrEmpty(_versionDoc))
                return;
            var saved = await _store.SaveDocument(_versionDoc, version, DocumentStoreVersion.At(_versionVer), null);
            if (!saved)
            {
                Logger.VersionSavingFailedDueToConcurrency(_versionDoc, _versionVer, version);
                throw new MetadataInstanceConcurrencyException(_versionDoc, _versionVer, version);
            }
            _versionVer++;
            Logger.VersionSaved(_versionVer, version);
        }
    }

    [Serializable]
    public class MetadataInstanceConcurrencyException : Exception
    {
        private readonly string _documentName;
        private readonly int _expectedVersion;
        private readonly object _dataToSave;

        public MetadataInstanceConcurrencyException(string documentName, int expectedVersion, object dataToSave)
            : base("Could not save metadata due to document store concurrency")
        {
            _documentName = documentName;
            _expectedVersion = expectedVersion;
            _dataToSave = dataToSave;
        }

        protected MetadataInstanceConcurrencyException(
            SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
            _documentName = info.GetString("DocumentName");
            _expectedVersion = info.GetInt32("ExpectedVersion");
            _dataToSave = info.GetString("DataToSave");
        }

        public string DocumentName
        {
            get { return _documentName; }
        }

        public int ExpectedVersion
        {
            get { return _expectedVersion; }
        }

        public object DataToSave
        {
            get { return _dataToSave; }
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("DocumentName", _documentName);
            info.AddValue("ExpectedVersion", _expectedVersion);
            info.AddValue("DataToSave", _dataToSave == null ? null : _dataToSave.ToString());
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
            var msg = new LogContextMessage(
                TraceEventType.Error, 3, "Token {Token} could not be saved due to concurrency");
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
            var msg = new LogContextMessage(
                TraceEventType.Error, 8, "Version {Version} could not be saved due to concurrency");
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