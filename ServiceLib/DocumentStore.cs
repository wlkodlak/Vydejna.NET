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
        void FindDocumentKeys(string indexName, string minValue, string maxValue, Action<IList<string>> onFoundKeys, Action<Exception> onError);
        void FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending, Action<DocumentStoreFoundDocuments> onFoundDocuments, Action<Exception> onError);
    }
    public interface IDocumentStore
    {
        IDocumentFolder GetFolder(string name);
    }
    public class DocumentStoreFoundDocuments : List<DocumentStoreFoundDocument>
    {
        public int TotalFound { get; set; }
    }
    public class DocumentStoreFoundDocument
    {
        public string Name { get; private set; }
        public int Version { get; private set; }
        public string Contents { get; private set; }

        public DocumentStoreFoundDocument(string name, int version, string contents)
        {
            Name = name;
            Version = version;
            Contents = contents;
        }
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

    public class FindDocumentKeysCompleted : IQueuedExecutionDispatcher
    {
        private Action<IList<string>> _onFoundKeys;
        private List<string> _list;

        public FindDocumentKeysCompleted(Action<IList<string>> onFoundKeys, List<string> list)
        {
            _onFoundKeys = onFoundKeys;
            _list = list;
        }

        public void Execute()
        {
            _onFoundKeys(_list);
        }
    }

    public class FindDocumentsCompleted : IQueuedExecutionDispatcher
    {
        private Action<DocumentStoreFoundDocuments> _onFoundDocuments;
        private DocumentStoreFoundDocuments _list;

        public FindDocumentsCompleted(Action<DocumentStoreFoundDocuments> onFoundDocuments, DocumentStoreFoundDocuments list)
        {
            _onFoundDocuments = onFoundDocuments;
            _list = list;
        }

        public void Execute()
        {
            _onFoundDocuments(_list);
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

        public DocumentIndexing(string indexName, string value)
        {
            IndexName = indexName;
            Values = new[] { value };
        }
    }

    public static class DocumentStoreUtils
    {
        public static string CreateBasicDocumentName(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return "__default__";
            int size = parts.Length;
            int[] lengths = new int[size];
            int[] offsets = new int[size];
            int totalLength = 0;
            for (int i = 0; i < size; i++)
            {
                int length = parts[i].Length;
                lengths[i] = length;
                offsets[i] = totalLength;
                totalLength += length;
            }
            var chars = new char[totalLength];
            for (int i = 0; i < size; i++)
            {
                parts[i].CopyTo(0, chars, offsets[i], lengths[i]);
            }
            for (int i = 0; i < totalLength; i++)
            {
                if (!IsCharacterAllowed(chars[i]))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static bool IsCharacterAllowed(char c)
        {
            if (c >= 'a' && c <= 'z')
                return true;
            if (c >= 'A' && c <= 'Z')
                return true;
            if (c >= '0' && c <= '9')
                return true;
            if (c == '_' || c == '-')
                return true;
            return false;
        }
    }
}
