using System;

namespace ServiceLib
{
    public interface IDocumentFolder
    {
        IDocumentFolder SubFolder(string name);
        void DeleteAll(Action onComplete, Action<Exception> onError);
        void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError);
        void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError);
        IDisposable WatchChanges(string name, Action onSomethingChanged);
    }
    public interface IDocumentStore : IDocumentFolder
    {
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
}
