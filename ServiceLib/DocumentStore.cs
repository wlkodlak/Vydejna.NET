using System;
using System.Collections.Generic;

namespace ServiceLib
{
    public interface IDocumentFolder
    {
        void DeleteAll(Action onComplete, Action<Exception> onError);
        void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError);
        void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError);
        void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes, Action onSave, Action onConcurrency, Action<Exception> onError);
        void FindDocuments(string indexName, string minValue, string maxValue, Action<IList<string>> onFoundKeys, Action<Exception> onError);
    }
    public interface IDocumentStore
    {
        IDocumentFolder GetFolder(string name);
    }
    public class DocumentStoreVersion
    {
        private readonly int _version;
        private static readonly DocumentStoreVersion _any = new DocumentStoreVersion(-1);
        private static readonly DocumentStoreVersion _new = new DocumentStoreVersion(0);
        private DocumentStoreVersion(int version) { _version = version; }
        public static DocumentStoreVersion Any { get { return _any; } }
        public static DocumentStoreVersion New { get { return _new; } }
        public static DocumentStoreVersion At(int version) { return new DocumentStoreVersion(version); }
        public override int GetHashCode()
        {
            return _version;
        }
        public override bool Equals(object obj)
        {
            var oth = obj as DocumentStoreVersion;
            return oth != null && _version == oth._version;
        }
        public override string ToString()
        {
            if (_version < 0)
                return "Any";
            else if (_version == 0)
                return "New";
            else
                return string.Format("Version {0}", _version);
        }
        public bool VerifyVersion(int actualVersion)
        {
            if (_version < 0)
                return true;
            else
                return _version == actualVersion;
        }
        public bool AllowNew { get { return _version <= 0; } }
    }

    public class DocumentFound : IQueuedExecutionDispatcher
    {
        private Action<int, string> _onFound;
        private int _version;
        private string _contents;

        public DocumentFound(Action<int, string> onFound, int version, string contents)
        {
            this._onFound = onFound;
            this._version = version;
            this._contents = contents;
        }

        public void Execute()
        {
            _onFound(_version, _contents);
        }
    }

    public class DocumentIndexing
    {
        public string IndexName { get; private set; }
        public IList<string> Values { get; private set; }

        public DocumentIndexing(string indexName, IList<string> values)
        {
            IndexName = indexName;
            Values = values;
        }
    }
}
