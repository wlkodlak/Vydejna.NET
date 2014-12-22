using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IDocumentFolder
    {
        Task DeleteAll();
        Task<DocumentStoreFoundDocument> GetDocument(string name);
        Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion);

        Task<bool> SaveDocument(
            string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes);

        Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue);

        Task<DocumentStoreFoundDocuments> FindDocuments(
            string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending);
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

        private DocumentStoreVersion(int version)
        {
            _version = version;
        }

        public static DocumentStoreVersion Any
        {
            get { return _any; }
        }

        public static DocumentStoreVersion New
        {
            get { return _new; }
        }

        public static DocumentStoreVersion At(int version)
        {
            return new DocumentStoreVersion(version);
        }

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

        public bool AllowNew
        {
            get { return _version <= 0; }
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
            Values = new[] {value};
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

    [Serializable]
    public class DocumentNameInvalidException : ArgumentException
    {
        private readonly string _documentName;

        public DocumentNameInvalidException(string documentName)
            : base("Invalid name for document")
        {
            _documentName = documentName;
        }

        protected DocumentNameInvalidException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            _documentName = info.GetString("DocumentName");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("DocumentName", _documentName);
        }
    }

    public class DocumentStoreTraceSource : TraceSource
    {
        public DocumentStoreTraceSource(string name)
            : base(name)
        {
        }

        public void DeletedFolder(string folderName)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 1, "Folder {FolderName} deleted");
            msg.SetProperty("FolderName", false, folderName);
            msg.Log(this);
        }

        public void DocumentNotFound(string folderName, string documentName)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 2, "Document {FolderName}/{DocumentName} not found");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.Log(this);
        }

        public void DocumentRetrieved(
            string folderName, string documentName, int documentVersion, string documentContents)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 3, "Document {FolderName}/{DocumentName} retrieved");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.SetProperty("DocumentContents", true, documentContents);
            msg.Log(this);
        }

        public void DocumentUpToDate(string folderName, string documentName, int documentVersion)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 4, "Document {FolderName}/{DocumentName} is still up to date");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.Log(this);
        }

        public void DocumentSaved(string folderName, string documentName, int documentVersion, string documentContents)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 5, "Document {FolderName}/{DocumentName} saved");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("DocumentVersion", false, documentVersion);
            msg.SetProperty("DocumentContents", true, documentContents);
            msg.Log(this);
        }

        public void DocumentConflicted(
            string folderName, string documentName, DocumentStoreVersion expectedVersion, int actualVersion)
        {
            var msg = new LogContextMessage(TraceEventType.Information, 9, "Document {FolderName}/{DocumentName} saved");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("ExpectedVersion", true, expectedVersion);
            msg.SetProperty("ActualVersion", false, actualVersion);
            msg.Log(this);
        }

        public void UpdatedIndex(string folderName, string indexName, IList<string> values)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 6, "Index {FolderName}/{IndexName} updated");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("IndexName", false, indexName);
            msg.SetProperty("Values", true, string.Join(", ", values));
            msg.Log(this);
        }

        public void FoundDocumentKeys(
            string folderName, string indexName, string minValue, string maxValue, ICollection<string> foundKeys)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 7,
                "Search in {FolderName}/{IndexName} for range from {MinValue} to {MaxValue} produced {FoundCount} keys");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("IndexName", false, indexName);
            msg.SetProperty("MinValue", false, minValue);
            msg.SetProperty("MaxValue", false, maxValue);
            msg.SetProperty("FoundCount", false, foundKeys.Count);
            msg.SetProperty("DocumentNames", true, string.Join(", ", foundKeys.Take(5)));
            msg.Log(this);
        }

        public void FoundDocuments(
            string folderName, string indexName, string minValue, string maxValue, int skip, int maxCount,
            bool ascending, DocumentStoreFoundDocuments result)
        {
            var msg = new LogContextMessage(
                TraceEventType.Information, 8,
                "Search in {FolderName}/{IndexName} for range from {MinValue} to {MaxValue} produced {FoundCount} documents");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("IndexName", false, indexName);
            msg.SetProperty("MinValue", false, minValue);
            msg.SetProperty("MaxValue", false, maxValue);
            msg.SetProperty("Skip", false, skip);
            msg.SetProperty("MaxCount", false, maxCount);
            msg.SetProperty("Ascending", false, ascending);
            msg.SetProperty("FoundCount", false, result.Count);
            msg.SetProperty("DocumentNames", true, string.Join(", ", result.Take(5).Select(d => d.Name)));
            msg.Log(this);
        }
    }

    public class DocumentStoreInMemoryTraceSource : DocumentStoreTraceSource
    {
        public DocumentStoreInMemoryTraceSource(string name)
            : base(name)
        {
        }
    }

    public class DocumentStorePostgresTraceSource : DocumentStoreTraceSource
    {
        public DocumentStorePostgresTraceSource(string name)
            : base(name)
        {
        }

        public void CreatedStorage(string partition, IList<string> storageParts)
        {
            if (storageParts == null || storageParts.Count == 0)
            {
                var msg = new LogContextMessage(
                    TraceEventType.Information, 101, "Storage for documents (partition {Partition}) already exists");
                msg.SetProperty("Partition", false, partition);
                msg.Log(this);
            }
            else
            {
                var msg = new LogContextMessage(
                    TraceEventType.Information, 102, "Created storage for documents (partition {Partition})");
                msg.SetProperty("Partition", false, partition);
                msg.SetProperty("Parts", false, string.Join(", ", storageParts));
                msg.Log(this);
            }
        }

        public void SaveDocumentWillRetry(string folderName, string documentName, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 103,
                "When saving document {FolderName}/{DocumentName}, attempt failed and will be retried.");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }

        public void UpdateIndexWillRetry(string folderName, string documentName, Exception exception)
        {
            var msg = new LogContextMessage(
                TraceEventType.Warning, 104,
                "When saving document {FolderName}/{DocumentName}, attempt to update indexes failed and will be retried.");
            msg.SetProperty("FolderName", false, folderName);
            msg.SetProperty("DocumentName", false, documentName);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }
}