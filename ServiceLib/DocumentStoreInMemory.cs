using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServiceLib
{
    public class DocumentStoreInMemory : IDocumentStore
    {
        private class Document
        {
            public object Lock;
            public string Value;
            public int Version;
            public Document(int version, string value)
            {
                Lock = new object();
                Version = version;
                Value = value;
            }
        }

        private class Index
        {
            public string IndexName;
            public SortedDictionary<string, HashSet<string>> ByDocument;
            public SortedDictionary<string, HashSet<string>> ByValue;

            public Index(string indexName)
            {
                IndexName = indexName;
                ByDocument = new SortedDictionary<string, HashSet<string>>();
                ByValue = new SortedDictionary<string, HashSet<string>>();
            }
        }

        private class DocumentFolder : IDocumentFolder
        {
            private string _folderName;
            private IQueueExecution _executor;
            private ConcurrentDictionary<string, Document> _documents = new ConcurrentDictionary<string, Document>();
            private ConcurrentDictionary<string, Index> _indexes = new ConcurrentDictionary<string, Index>();

            public DocumentFolder(string folderName, IQueueExecution executor)
            {
                _folderName = folderName;
                _executor = executor;
            }

            public void DeleteAll(Action onComplete, Action<Exception> onError)
            {
                List<string> documentNames = _documents.Keys.ToList();
                _documents.Clear();
                _indexes.Clear();
                _executor.Enqueue(onComplete);
            }

            public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                try
                {
                    if (!_nameRegex.IsMatch(name))
                        throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                    Document document;
                    if (!_documents.TryGetValue(name, out document))
                        _executor.Enqueue(onMissing);
                    else if (document.Version == 0)
                        _executor.Enqueue(onMissing);
                    else
                        _executor.Enqueue(new DocumentFound(onFound, document.Version, document.Value));
                }
                catch (Exception ex)
                {
                    _executor.Enqueue(onError, ex);
                }
            }

            public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
            {
                try
                {
                    if (!_nameRegex.IsMatch(name))
                        throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                    Document document;
                    if (!_documents.TryGetValue(name, out document))
                        _executor.Enqueue(onMissing);
                    else if (document.Version == 0)
                        _executor.Enqueue(onMissing);
                    else if (document.Version == knownVersion)
                        _executor.Enqueue(onNotModified);
                    else
                        _executor.Enqueue(new DocumentFound(onFoundNewer, document.Version, document.Value));
                }
                catch (Exception ex)
                {
                    _executor.Enqueue(onError, ex);
                }
            }

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                bool wasSaved = false;
                int newVersion = 0;
                try
                {
                    if (!_nameRegex.IsMatch(name))
                        throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                    var newDocument = new Document(0, null);
                    var existingDocument = _documents.GetOrAdd(name, newDocument);
                    lock (existingDocument.Lock)
                    {
                        wasSaved = expectedVersion.VerifyVersion(existingDocument.Version);
                        if (wasSaved)
                        {
                            existingDocument.Version++;
                            existingDocument.Value = value;
                            newVersion = existingDocument.Version;
                        }
                    }
                    if (indexes != null)
                    {
                        foreach (var indexChange in indexes)
                        {
                            Index index;
                            if (!_indexes.TryGetValue(indexChange.IndexName, out index))
                                index = _indexes.GetOrAdd(indexChange.IndexName, new Index(indexChange.IndexName));
                            lock (index)
                            {
                                HashSet<string> existingValues, valueDocuments;
                                if (!index.ByDocument.TryGetValue(name, out existingValues))
                                {
                                    index.ByDocument[name] = new HashSet<string>(indexChange.Values);
                                    foreach (var idxValue in indexChange.Values)
                                    {
                                        if (!index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                            index.ByValue[idxValue] = valueDocuments = new HashSet<string>();
                                        valueDocuments.Add(name);
                                    }
                                }
                                else
                                {
                                    foreach (var idxValue in existingValues)
                                    {
                                        if (indexChange.Values.Contains(idxValue))
                                            continue;
                                        if (index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                            valueDocuments.Remove(name);
                                    }
                                    foreach (var idxValue in indexChange.Values)
                                    {
                                        if (existingValues.Contains(idxValue))
                                            continue;
                                        existingValues.Add(idxValue);
                                        if (!index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                            index.ByValue[idxValue] = valueDocuments = new HashSet<string>();
                                        valueDocuments.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    if (wasSaved)
                        _executor.Enqueue(onSave);
                    else
                        _executor.Enqueue(onConcurrency);
                }
                catch (Exception ex)
                {
                    _executor.Enqueue(onError, ex);
                }
            }

            public void FindDocumentKeys(string indexName, string minValue, string maxValue, Action<IList<string>> onFoundKeys, Action<Exception> onError)
            {
                Index index;
                if (!_indexes.TryGetValue(indexName, out index))
                {
                    onError(new ArgumentOutOfRangeException("indexName", string.Format("Index {0} does not exist in folder {1}", indexName, _folderName)));
                    return;
                }
                var foundKeys = new List<string>();
                if (string.Equals(minValue, maxValue, StringComparison.Ordinal))
                {
                    HashSet<string> foundDocs;
                    if (index.ByValue.TryGetValue(minValue, out foundDocs))
                    {
                        foundKeys.AddRange(foundDocs);
                    }
                }
                else
                {
                    foreach (var pair in index.ByValue)
                    {
                        if (string.CompareOrdinal(pair.Key, minValue) < 0)
                            continue;
                        if (string.CompareOrdinal(pair.Key, maxValue) > 0)
                            continue;
                        foundKeys.AddRange(pair.Value);
                    }
                }
                onFoundKeys(foundKeys);
            }


            public void FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending, Action<DocumentStoreFoundDocuments> onFoundDocuments, Action<Exception> onError)
            {
                throw new NotImplementedException();
            }
        }

        private IQueueExecution _executor;
        private ConcurrentDictionary<string, DocumentFolder> _folders = new ConcurrentDictionary<string, DocumentFolder>();
        private static readonly Regex _nameRegex = new Regex(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);

        public DocumentStoreInMemory(IQueueExecution executor)
        {
            _executor = executor;
            _folders = new ConcurrentDictionary<string, DocumentFolder>();
        }

        public IDocumentFolder GetFolder(string name)
        {
            if (!_nameRegex.IsMatch(name))
                throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
            DocumentFolder folder;
            while (true)
            {
                if (_folders.TryGetValue(name, out folder))
                    return folder;
                else if (_folders.TryAdd(name, (folder = new DocumentFolder(name, _executor))))
                    return folder;
            }
        }

    }

}
