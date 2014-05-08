using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IDocumentFolder
    {
        Task DeleteAll();
        Task<DocumentStoreFoundDocument> GetDocument(string name);
        Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion);
        Task<bool> SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes);
        Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue);
        Task<DocumentStoreFoundDocuments> FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending);
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
        public bool HasNewerContent { get; private set; }
        public string Contents { get; private set; }

        public DocumentStoreFoundDocument(string name, int version, bool hasNewerContent, string contents)
        {
            Name = name;
            Version = version;
            HasNewerContent = hasNewerContent;
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
