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

        private class FolderWatcher : IDisposable
        {
            private FolderWatcherCollection _parent;
            public int Key;
            public int SinceVersion;
            public Action OnSomethingChanged;

            public FolderWatcher(FolderWatcherCollection parent, int key, int sinceVersion, Action onSomethingChanged)
            {
                _parent = parent;
                Key = key;
                SinceVersion = sinceVersion;
                OnSomethingChanged = onSomethingChanged;
            }

            public void Dispose()
            {
                _parent.Remove(Key);
            }
        }

        private class FolderWatcherCollection
        {
            private int _key;
            private ConcurrentDictionary<int, FolderWatcher> _watchers;
            private IQueueExecution _executor;

            public FolderWatcherCollection(IQueueExecution executor)
            {
                _key = 0;
                _watchers = new ConcurrentDictionary<int, FolderWatcher>();
                _executor = executor;
            }

            public IDisposable AddWatcher(int sinceVersion, Action onSomethingChanged)
            {
                Interlocked.Increment(ref _key);
                var watcher = new FolderWatcher(this, _key, sinceVersion, onSomethingChanged);
                _watchers.TryAdd(_key, watcher);
                return watcher;
            }

            public void CallWatchers(int newVersion)
            {
                foreach (var watcher in _watchers.Values)
                {
                    if (watcher.SinceVersion != newVersion)
                        _executor.Enqueue(watcher.OnSomethingChanged);
                }
            }

            public void Remove(int key)
            {
                FolderWatcher removed;
                _watchers.TryRemove(key, out removed);
            }
        }

        private class DocumentFolder : IDocumentFolder
        {
            private static readonly Regex _regex = new Regex(@"^[a-zA-Z0-9_\-.]+$", RegexOptions.Compiled);
            private IQueueExecution _executor;
            private ConcurrentDictionary<string, DocumentFolder> _folders = new ConcurrentDictionary<string, DocumentFolder>();
            private ConcurrentDictionary<string, Document> _documents = new ConcurrentDictionary<string, Document>();
            private ConcurrentDictionary<string, FolderWatcherCollection> _watchers = new ConcurrentDictionary<string, FolderWatcherCollection>();

            public DocumentFolder(IQueueExecution executor)
            {
                _executor = executor;
            }

            public IDocumentFolder SubFolder(string name)
            {
                if (!_regex.IsMatch(name))
                    throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                DocumentFolder folder;
                while (true)
                {
                    if (_folders.TryGetValue(name, out folder))
                        return folder;
                    else if (_folders.TryAdd(name, (folder = new DocumentFolder(_executor))))
                        return folder;
                }
            }

            public void DeleteAll(Action onComplete, Action<Exception> onError)
            {
                List<string> documentNames = _documents.Keys.ToList();
                foreach (var folder in _folders)
                    folder.Value.DeleteAll(() => { }, ex => { });
                _folders.Clear();
                _documents.Clear();
                _executor.Enqueue(onComplete);
                foreach (string name in documentNames)
                    CallWatchers(name, 0);
            }

            public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                try
                {
                    if (!_regex.IsMatch(name))
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
                    if (!_regex.IsMatch(name))
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

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                bool wasSaved = false;
                int newVersion = 0;
                try
                {
                    if (!_regex.IsMatch(name))
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
                    if (wasSaved)
                        _executor.Enqueue(onSave);
                    else
                        _executor.Enqueue(onConcurrency);
                }
                catch (Exception ex)
                {
                    _executor.Enqueue(onError, ex);
                }
                if (wasSaved)
                    CallWatchers(name, newVersion);
            }

            public IDisposable WatchChanges(string name, int sinceVersion, Action onSomethingChanged)
            {
                int currentVersion;
                Document document;
                if (!_documents.TryGetValue(name, out document))
                    currentVersion = 0;
                else
                    currentVersion = document.Version;

                var watcherCollection = _watchers.GetOrAdd(name, new FolderWatcherCollection(_executor));
                var watcher = watcherCollection.AddWatcher(sinceVersion, onSomethingChanged);
                if (currentVersion != sinceVersion)
                    _executor.Enqueue(onSomethingChanged);
                return watcher;
            }

            private void CallWatchers(string name, int version)
            {
                FolderWatcherCollection collection;
                if (_watchers.TryGetValue(name, out collection))
                    collection.CallWatchers(version);
            }

        }

        private readonly DocumentFolder _root;

        public DocumentStoreInMemory(IQueueExecution executor)
        {
            _root = new DocumentFolder(executor);
        }

        public IDocumentFolder SubFolder(string name)
        {
            return _root.SubFolder(name);
        }

        public void DeleteAll(Action onComplete, Action<Exception> onError)
        {
            _root.DeleteAll(onComplete, onError);
        }

        public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
        {
            _root.GetDocument(name, onFound, onMissing, onError);
        }

        public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
        {
            _root.GetNewerDocument(name, knownVersion, onFoundNewer, onNotModified, onMissing, onError);
        }

        public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
        {
            _root.SaveDocument(name, value, expectedVersion, onSave, onConcurrency, onError);
        }

        public IDisposable WatchChanges(string name, int sinceVersion, Action onSomethingChanged)
        {
            return _root.WatchChanges(name, sinceVersion, onSomethingChanged);
        }
    }

}
