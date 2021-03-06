﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class DocumentStoreInMemory : IDocumentStore
    {
        private class Document
        {
            public readonly object Lock;
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
            public readonly SortedDictionary<string, HashSet<string>> ByDocument;
            public readonly SortedDictionary<string, HashSet<string>> ByValue;

            public Index()
            {
                ByDocument = new SortedDictionary<string, HashSet<string>>();
                ByValue = new SortedDictionary<string, HashSet<string>>();
            }
        }

        private class DocumentFolder : IDocumentFolder
        {
            private readonly string _folderName;

            private readonly ConcurrentDictionary<string, Document> _documents =
                new ConcurrentDictionary<string, Document>();

            private readonly ConcurrentDictionary<string, Index> _indexes = new ConcurrentDictionary<string, Index>();

            private static readonly DocumentStoreInMemoryTraceSource Logger =
                new DocumentStoreInMemoryTraceSource("ServiceLib.DocumentStore");

            public DocumentFolder(string folderName)
            {
                _folderName = folderName;
            }

            public Task DeleteAll()
            {
                _documents.Clear();
                _indexes.Clear();
                Logger.DeletedFolder(_folderName);
                return TaskUtils.CompletedTask();
            }

            public Task<DocumentStoreFoundDocument> GetDocument(string name)
            {
                var tcs = new TaskCompletionSource<DocumentStoreFoundDocument>();
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        tcs.SetException(new DocumentNameInvalidException(name));
                    }
                    else
                    {
                        Document document;
                        if (!_documents.TryGetValue(name, out document))
                        {
                            Logger.DocumentNotFound(_folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == 0)
                        {
                            Logger.DocumentNotFound(_folderName, name);
                            tcs.SetResult(null);
                        }
                        else
                        {
                            Logger.DocumentRetrieved(_folderName, name, document.Version, document.Value);
                            tcs.SetResult(new DocumentStoreFoundDocument(name, document.Version, true, document.Value));
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                return tcs.Task;
            }

            public Task<DocumentStoreFoundDocument> GetNewerDocument(string name, int knownVersion)
            {
                var tcs = new TaskCompletionSource<DocumentStoreFoundDocument>();
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        tcs.SetException(new DocumentNameInvalidException(name));
                    }
                    else
                    {
                        Document document;
                        if (!_documents.TryGetValue(name, out document))
                        {
                            Logger.DocumentNotFound(_folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == 0)
                        {
                            Logger.DocumentNotFound(_folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == knownVersion)
                        {
                            Logger.DocumentUpToDate(_folderName, name, document.Version);
                            tcs.SetResult(new DocumentStoreFoundDocument(name, document.Version, false, null));
                        }
                        else
                        {
                            Logger.DocumentRetrieved(_folderName, name, document.Version, document.Value);
                            tcs.SetResult(new DocumentStoreFoundDocument(name, document.Version, true, document.Value));
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                return tcs.Task;
            }

            public Task<bool> SaveDocument(
                string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes)
            {
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        return TaskUtils.FromError<bool>(new DocumentNameInvalidException(name));
                    }
                    var wasSaved = SaveDocumentCore(name, value, expectedVersion);
                    if (wasSaved)
                        UpdateIndexes(name, indexes);
                    return TaskUtils.FromResult(wasSaved);
                }
                catch (Exception ex)
                {
                    return TaskUtils.FromError<bool>(ex);
                }
            }

            private bool SaveDocumentCore(string name, string value, DocumentStoreVersion expectedVersion)
            {
                var newDocument = new Document(0, null);
                var existingDocument = _documents.GetOrAdd(name, newDocument);
                lock (existingDocument.Lock)
                {
                    var wasSaved = expectedVersion.VerifyVersion(existingDocument.Version);
                    if (wasSaved)
                    {
                        existingDocument.Version++;
                        existingDocument.Value = value;
                        Logger.DocumentSaved(_folderName, name, existingDocument.Version, existingDocument.Value);
                        return true;
                    }
                    else
                    {
                        Logger.DocumentConflicted(_folderName, name, expectedVersion, existingDocument.Version);
                        return false;
                    }
                }
            }

            private void UpdateIndexes(string documentName, IList<DocumentIndexing> indexes)
            {
                if (indexes == null) return;
                foreach (var indexChange in indexes)
                {
                    Index index;
                    if (!_indexes.TryGetValue(indexChange.IndexName, out index))
                        index = _indexes.GetOrAdd(indexChange.IndexName, new Index());
                    lock (index)
                    {
                        HashSet<string> existingValues, valueDocuments;
                        if (!index.ByDocument.TryGetValue(documentName, out existingValues))
                        {
                            index.ByDocument[documentName] = new HashSet<string>(indexChange.Values);
                            foreach (var idxValue in indexChange.Values)
                            {
                                if (!index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                    index.ByValue[idxValue] = valueDocuments = new HashSet<string>();
                                valueDocuments.Add(documentName);
                            }
                        }
                        else
                        {
                            foreach (var idxValue in existingValues)
                            {
                                if (indexChange.Values.Contains(idxValue))
                                    continue;
                                if (index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                    valueDocuments.Remove(documentName);
                            }
                            foreach (var idxValue in indexChange.Values)
                            {
                                if (existingValues.Contains(idxValue))
                                    continue;
                                existingValues.Add(idxValue);
                                if (!index.ByValue.TryGetValue(idxValue, out valueDocuments))
                                    index.ByValue[idxValue] = valueDocuments = new HashSet<string>();
                                valueDocuments.Add(documentName);
                            }
                        }
                        Logger.UpdatedIndex(_folderName, indexChange.IndexName, indexChange.Values);
                    }
                }
            }

            public Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue)
            {
                Index index;
                if (!_indexes.TryGetValue(indexName, out index))
                {
                    return TaskUtils.FromResult<IList<string>>(new string[0]);
                }
                var foundKeys = new HashSet<string>();
                if (string.Equals(minValue, maxValue, StringComparison.Ordinal))
                {
                    HashSet<string> foundDocs;
                    if (index.ByValue.TryGetValue(minValue, out foundDocs))
                    {
                        foreach (var doc in foundDocs)
                            foundKeys.Add(doc);
                    }
                }
                else
                {
                    foreach (var pair in index.ByValue)
                    {
                        if (minValue != null && string.CompareOrdinal(pair.Key, minValue) < 0)
                            continue;
                        if (maxValue != null && string.CompareOrdinal(pair.Key, maxValue) > 0)
                            continue;
                        foreach (var doc in pair.Value)
                            foundKeys.Add(doc);
                    }
                }
                Logger.FoundDocumentKeys(_folderName, indexName, minValue, maxValue, foundKeys);
                return TaskUtils.FromResult<IList<string>>(foundKeys.ToList());
            }

            public Task<DocumentStoreFoundDocuments> FindDocuments(
                string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
            {
                try
                {
                    Index index;
                    var result = new DocumentStoreFoundDocuments();
                    if (_indexes.TryGetValue(indexName, out index))
                    {
                        var byValueOrdered = ascending
                            ? index.ByValue.OrderBy(t => t.Key)
                            : index.ByValue.OrderByDescending(t => t.Key);
                        FillFromIndex(minValue, maxValue, skip, maxCount, byValueOrdered, result);
                        Logger.FoundDocuments(
                            _folderName, indexName, minValue, maxValue, skip, maxCount, ascending, result);
                    }
                    return TaskUtils.FromResult(result);
                }
                catch (Exception ex)
                {
                    return TaskUtils.FromError<DocumentStoreFoundDocuments>(ex);
                }
            }

            private void FillFromIndex(
                string minValue, string maxValue, int skip, int maxCount,
                IEnumerable<KeyValuePair<string, HashSet<string>>> byValueOrdered,
                DocumentStoreFoundDocuments result)
            {
                var allKeys = new HashSet<string>();
                foreach (var pair in byValueOrdered)
                {
                    if (minValue != null && string.CompareOrdinal(minValue, pair.Key) > 0)
                        continue;
                    if (maxValue != null && string.CompareOrdinal(pair.Key, maxValue) > 0)
                        continue;
                    foreach (var documentName in pair.Value)
                    {
                        if (!allKeys.Add(documentName))
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
                        if (_documents.TryGetValue(documentName, out document))
                        {
                            result.Add(
                                new DocumentStoreFoundDocument(documentName, document.Version, true, document.Value));
                        }
                    }
                }
                result.TotalFound = allKeys.Count;
            }
        }

        private readonly ConcurrentDictionary<string, DocumentFolder> _folders;
        private static readonly Regex _nameRegex = new Regex(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);

        public DocumentStoreInMemory()
        {
            _folders = new ConcurrentDictionary<string, DocumentFolder>();
        }

        public IDocumentFolder GetFolder(string name)
        {
            if (!_nameRegex.IsMatch(name))
                throw new DocumentNameInvalidException(name);
            DocumentFolder folder;
            while (true)
            {
                if (_folders.TryGetValue(name, out folder))
                    return folder;
                else if (_folders.TryAdd(name, (folder = new DocumentFolder(name))))
                    return folder;
            }
        }
    }
}