﻿using log4net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
            private readonly string _folderName;
            private readonly ConcurrentDictionary<string, Document> _documents = new ConcurrentDictionary<string, Document>();
            private readonly ConcurrentDictionary<string, Index> _indexes = new ConcurrentDictionary<string, Index>();
            private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.DocumentStore");

            public DocumentFolder(string folderName)
            {
                _folderName = folderName;
            }

            public Task DeleteAll()
            {
                Logger.DebugFormat("Deleting folder {0}", _folderName);
                List<string> documentNames = _documents.Keys.ToList();
                _documents.Clear();
                _indexes.Clear();
                return TaskUtils.CompletedTask();
            }

            public Task<DocumentStoreFoundDocument> GetDocument(string name)
            {
                Logger.DebugFormat("Entering GetDocument({0}/{1})", _folderName, name);
                var tcs = new TaskCompletionSource<DocumentStoreFoundDocument>();
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        tcs.SetException(new ArgumentOutOfRangeException(name, "Invalid characters in name"));
                    }
                    else
                    {
                        Document document;
                        if (!_documents.TryGetValue(name, out document))
                        {
                            Logger.DebugFormat("Document {0}/{1} not found", _folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == 0)
                        {
                            Logger.DebugFormat("Document {0}/{1} not found", _folderName, name);
                            tcs.SetResult(null);
                        }
                        else
                        {
                            Logger.DebugFormat("Document {0}/{1} returned at version {2}", _folderName, name, document.Version);
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
                Logger.DebugFormat("Entering GetNewerDocument({0}/{1}, {2})", _folderName, name, knownVersion);
                var tcs = new TaskCompletionSource<DocumentStoreFoundDocument>();
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        tcs.SetException(new ArgumentOutOfRangeException(name, "Invalid characters in name"));
                    }
                    else
                    {
                        Document document;
                        if (!_documents.TryGetValue(name, out document))
                        {
                            Logger.DebugFormat("Document {0}/{1} not found", _folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == 0)
                        {
                            Logger.DebugFormat("Document {0}/{1} not found", _folderName, name);
                            tcs.SetResult(null);
                        }
                        else if (document.Version == knownVersion)
                        {
                            Logger.DebugFormat("Document {0}/{1} is up to date at version {2}", _folderName, name, document.Version);
                            tcs.SetResult(new DocumentStoreFoundDocument(name, document.Version, false, null));
                        }
                        else
                        {
                            Logger.DebugFormat("Document {0}/{1} returned at version {2}", _folderName, name, document.Version);
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

            public Task<bool> SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, IList<DocumentIndexing> indexes)
            {
                Logger.DebugFormat("Entering SaveDocument({0}/{1}, ...)", _folderName, name);
                bool wasSaved = false;
                int newVersion = 0;
                try
                {
                    if (!_nameRegex.IsMatch(name))
                    {
                        return TaskUtils.FromError<bool>(new ArgumentOutOfRangeException(name, "Invalid characters in name"));
                    }
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
                            Logger.DebugFormat("Document {0}/{1} saved at version {2}", _folderName, name, existingDocument.Version);
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
                                Logger.DebugFormat("Updating index {0}/{1}", _folderName, indexChange.IndexName);
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
                    return TaskUtils.FromResult(wasSaved);
                }
                catch (Exception ex)
                {
                    return TaskUtils.FromError<bool>(ex);
                }
            }

            public Task<IList<string>> FindDocumentKeys(string indexName, string minValue, string maxValue)
            {
                Logger.DebugFormat("Entering FindDocumentKeys({0}/{1}, {2}, {3})", _folderName, indexName, minValue, maxValue);
                Index index;
                if (!_indexes.TryGetValue(indexName, out index))
                {
                    return TaskUtils.FromError<IList<string>>(new ArgumentOutOfRangeException("indexName", string.Format("Index {0} does not exist in folder {1}", indexName, _folderName)));
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
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugFormat("Found {0} keys: {1}", foundKeys.Count, string.Join(", ", foundKeys.Take(5)));
                }
                return TaskUtils.FromResult<IList<string>>(foundKeys.ToList());
            }

            public Task<DocumentStoreFoundDocuments> FindDocuments(string indexName, string minValue, string maxValue, int skip, int maxCount, bool ascending)
            {
                Logger.DebugFormat("Entering FindDocuments({0}/{1}, {2}, {3}, {4}, {5}, {6})", _folderName, indexName, minValue, maxValue, skip, maxCount, ascending);
                try
                {
                    Index index;
                    var result = new DocumentStoreFoundDocuments();
                    if (_indexes.TryGetValue(indexName, out index))
                    {
                        var allKeys = new HashSet<string>();
                        var byValueOrdered = ascending ? index.ByValue.OrderBy(t => t.Key) : index.ByValue.OrderByDescending(t => t.Key);
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
                                    result.Add(new DocumentStoreFoundDocument(documentName, document.Version, true, document.Value));
                                }
                            }
                        }
                        result.TotalFound = allKeys.Count;
                        if (Logger.IsDebugEnabled)
                        {
                            Logger.DebugFormat("Found {0} documents: {1}", result.TotalFound, string.Join(", ", result.Take(5).Select(d => d.Name)));
                        }
                    }
                    return TaskUtils.FromResult(result);
                }
                catch (Exception ex)
                {
                    return TaskUtils.FromError<DocumentStoreFoundDocuments>(ex);
                }
            }
        }

        private ConcurrentDictionary<string, DocumentFolder> _folders = new ConcurrentDictionary<string, DocumentFolder>();
        private static readonly Regex _nameRegex = new Regex(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);

        public DocumentStoreInMemory()
        {
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
                else if (_folders.TryAdd(name, (folder = new DocumentFolder(name))))
                    return folder;
            }
        }

    }

}
