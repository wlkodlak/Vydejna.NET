using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IDocumentFolder
    {
        IDocumentFolder SubFolder(string name);
        void DeleteAll(Action onComplete);
        void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError);
        void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError);
        IDisposable WatchChanges(Action onSomethingChanged);
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
            private ConcurrentDictionary<int, FolderWatcher> _watchers;
            private int _key;
            private Action _onSomethingChanged;

            public FolderWatcher(ConcurrentDictionary<int, FolderWatcher> watchersCollection, int key, Action onSomethingChanged)
            {
                _key = key;
                _watchers = watchersCollection;
                _onSomethingChanged = onSomethingChanged;
            }

            public void Dispose()
            {
                FolderWatcher removedValue;
                _watchers.TryRemove(_key, out removedValue);
            }

            public void CallHandler()
            {
                try
                {
                    _onSomethingChanged();
                }
                catch
                {
                    Dispose();
                }
            }
        }

        private class DocumentFolder : IDocumentFolder
        {
            private static readonly Regex _regex = new Regex(@"^[a-zA-Z0-9_\-.]+$", RegexOptions.Compiled);
            private ConcurrentDictionary<string, DocumentFolder> _folders = new ConcurrentDictionary<string, DocumentFolder>();
            private ConcurrentDictionary<string, Document> _documents = new ConcurrentDictionary<string, Document>();
            private int _watcherNumber = 0;
            private ConcurrentDictionary<int, FolderWatcher> _watchers = new ConcurrentDictionary<int,FolderWatcher>();

            public IDocumentFolder SubFolder(string name)
            {
                if (!_regex.IsMatch(name))
                    throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                DocumentFolder folder;
                while (true)
                {
                    if (_folders.TryGetValue(name, out folder))
                        return folder;
                    else if (_folders.TryAdd(name, (folder = new DocumentFolder())))
                        return folder;
                }
            }

            public void DeleteAll(Action onComplete)
            {
                foreach (var folder in _folders)
                    folder.Value.DeleteAll(() => { });
                _folders.Clear();
                _documents.Clear();
                onComplete();
                CallWatchers();
            }

            public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
            {
                try
                {
                    if (!_regex.IsMatch(name))
                        throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                    Document document;
                    if (!_documents.TryGetValue(name, out document))
                        onMissing();
                    else if (document.Version == 0)
                        onMissing();
                    else
                        onFound(document.Version, document.Value);
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
            }

            public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
            {
                try
                {
                    if (!_regex.IsMatch(name))
                        throw new ArgumentOutOfRangeException(name, "Invalid characters in name");
                    var newDocument = new Document(0, null);
                    var existingDocument = _documents.GetOrAdd(name, newDocument);
                    bool wasSaved = false;
                    lock (existingDocument.Lock)
                    {
                        wasSaved = expectedVersion.VerifyVersion(existingDocument.Version);
                        if (wasSaved)
                        {
                            existingDocument.Version++;
                            existingDocument.Value = value;
                        }
                    }
                    if (wasSaved)
                        onSave();
                    else
                        onConcurrency();
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                CallWatchers();
            }

            public IDisposable WatchChanges(Action onSomethingChanged)
            {
                int key = Interlocked.Increment(ref _watcherNumber);
                var watcher = new FolderWatcher(_watchers, key, onSomethingChanged);
                _watchers.TryAdd(key, watcher);
                return watcher;
            }

            private void CallWatchers()
            {
                var watchers = _watchers.Values.ToList();
                foreach (var watcher in watchers)
                    watcher.CallHandler();
            }
        }

        private readonly DocumentFolder _root = new DocumentFolder();

        public IDocumentFolder SubFolder(string name)
        {
            return _root.SubFolder(name);
        }

        public void DeleteAll(Action onComplete)
        {
            _root.DeleteAll(onComplete);
        }

        public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
        {
            _root.GetDocument(name, onFound, onMissing, onError);
        }

        public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
        {
            _root.SaveDocument(name, value, expectedVersion, onSave, onConcurrency, onError);
        }

        public IDisposable WatchChanges(Action onSomethingChanged)
        {
            return _root.WatchChanges(onSomethingChanged);
        }
    }
}
