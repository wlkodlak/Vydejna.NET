using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Tests.TestUtils
{
    public class TestDocumentFolder : IDocumentFolder
    {
        private object _lock;
        private Dictionary<string, Document> _data;
        private List<string> _log;
        private Dictionary<string, Index> _indexes;

        private class Document
        {
            public int Version;
            public string Contents;
            public override string ToString()
            {
                return string.Format("@{0}: {1}", Version, Contents);
            }
        }

        private class Index : List<Tuple<string, string>>
        {
        }

        public TestDocumentFolder()
        {
            _lock = new object();
            _data = new Dictionary<string, Document>();
            _log = new List<string>();
            _indexes = new Dictionary<string, Index>();
        }

        public Task DeleteAll()
        {
            lock (_lock)
            {
                _data.Clear();
                _indexes.Clear();
                AddToLog("Clear", null, 0);
                return TaskUtils.CompletedTask();
            }
        }

        public void ClearLog()
        {
            _log.Clear();
        }
        private void AddToLog(string action, string document, int version)
        {
            var entry = document != null ? string.Format("{0} {1}@{2}", action, document, version) : action;
            _log.Add(entry);
        }

        public string Log
        {
            get
            {
                var sb = new StringBuilder();
                _log.ForEach(l => sb.AppendLine(l));
                return sb.ToString();
            }
        }
        public List<string> LogLines()
        {
            return _log;
        }

        public Task<DocumentStoreFoundDocument> GetDocument(string name)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    AddToLog("Get", name, document.Version);
                    return TaskUtils.FromResult(new DocumentStoreFoundDocument(name, document.Version, true, document.Contents));
                }
                else
                {
                    AddToLog("Get", name, 0);
                    return TaskUtils.FromResult(new DocumentStoreFoundDocument(name, 0, true, ""));
                }
            }
        }

        public Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    AddToLog("Get", name, document.Version);
                    if (document.Version == knownVersion)
                        return TaskUtils.FromResult(new DocumentStoreFoundDocument(name, document.Version, false, null));
                    else
                        return TaskUtils.FromResult(new DocumentStoreFoundDocument(name, document.Version, true, document.Contents));
                }
                else
                {
                    AddToLog("Get", name, 0);
                    return TaskUtils.FromResult(new DocumentStoreFoundDocument(name, 0, true, ""));
                }
            }
        }

        public string GetDocumentSync(string name)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                    return document.Contents;
                else
                    return null;
            }
        }

        public int GetVersionSync(string name)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                    return document.Version;
                else
                    return 0;
            }
        }

        public Task<bool> SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    if (!expectedVersion.VerifyVersion(document.Version))
                    {
                        AddToLog("Conflict", name, document.Version);
                        return TaskUtils.FromResult(false);
                    }
                    else
                    {
                        document.Version++;
                        document.Contents = value;
                        AddToLog("Save", name, document.Version);
                        SaveIndexes(name, indexes);
                        return TaskUtils.FromResult(true);
                    }
                }
                else
                {
                    if (!expectedVersion.VerifyVersion(0))
                    {
                        AddToLog("Conflict", name, 0);
                        return TaskUtils.FromResult(false);
                    }
                    else
                    {
                        _data[name] = document = new Document();
                        document.Version = 1;
                        document.Contents = value;
                        AddToLog("Save", name, 1);
                        SaveIndexes(name, indexes);
                        return TaskUtils.FromResult(true);
                    }
                }
            }
        }

        public void SaveDocumentSync(string name, string value, int manualVersion = -1, IList<DocumentIndexing> indexes = null)
        {
            lock (_lock)
            {
                Document document;
                if (!_data.TryGetValue(name, out document))
                    _data[name] = document = new Document();
                document.Version = (manualVersion >= 0) ? manualVersion : document.Version + 1;
                document.Contents = value;
                SaveIndexes(name, indexes);
            }
        }

        private void SaveIndexes(string name, IList<DocumentIndexing> indexes)
        {
            if (indexes == null || indexes.Count == 0)
                return;
            foreach (var indexChanges in indexes)
            {
                Index index;
                if (!_indexes.TryGetValue(indexChanges.IndexName, out index))
                    _indexes[indexChanges.IndexName] = index = new Index();
                index.RemoveAll(t => string.Equals(t.Item2, name));
                index.AddRange(indexChanges.Values.Select(v => Tuple.Create(v, name)));
                index.Sort((x, y) => string.CompareOrdinal(x.Item1, y.Item1));
            }
        }

        public Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue)
        {
            List<string> result;
            Index index;
            if (_indexes.TryGetValue(indexName, out index))
            {
                var query = index.AsEnumerable();
                if (minValue != null)
                    query = query.Where(t => string.CompareOrdinal(minValue, t.Item1) <= 0);
                if (maxValue != null)
                    query = query.Where(t => string.CompareOrdinal(t.Item1, maxValue) <= 0);
                result = query.Select(t => t.Item2).Distinct().ToList();
            }
            else
                result = new List<string>();
            return TaskUtils.FromResult<IList<string>>(result);
        }

        public Task<DocumentStoreFoundDocuments> FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
        {
            Index index;
            var result = new DocumentStoreFoundDocuments();
            if (_indexes.TryGetValue(indexName, out index))
            {
                var allKeys = new HashSet<string>();
                foreach (var pair in index.OrderBy(t => t.Item1))
                {
                    if (minValue != null && string.CompareOrdinal(minValue, pair.Item1) > 0)
                        continue;
                    if (maxValue != null && string.CompareOrdinal(pair.Item1, maxValue) > 0)
                        continue;
                    if (!allKeys.Add(pair.Item2))
                        continue;
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }
                    if (maxCount == 0)
                        continue;
                    maxCount--;
                    Document document;
                    if (_data.TryGetValue(pair.Item2, out document))
                    {
                        result.Add(new DocumentStoreFoundDocument(pair.Item1, document.Version, true, document.Contents));
                    }
                }
                result.TotalFound = allKeys.Count;
            }
            return TaskUtils.FromResult(result);
        }
    }
}
