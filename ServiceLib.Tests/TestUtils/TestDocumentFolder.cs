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
        private List<Watcher> _watchers;
        private IQueueExecution _executor;

        private class Watcher : IDisposable
        {
            public TestDocumentFolder Parent;
            public string Document;
            public Action OnChange;
            public void Dispose() { Parent.UnWatch(this); }
        }
        private class Document
        {
            public int Version;
            public string Contents;
        }

        public TestDocumentFolder(IQueueExecution executor)
        {
            _executor = executor;
            _lock = new object();
            _data = new Dictionary<string, Document>();
            _watchers = new List<Watcher>();
        }

        public IDocumentFolder SubFolder(string name)
        {
            throw new NotSupportedException("Subfolders are not supported");
        }

        public void DeleteAll(Action onComplete, Action<Exception> onError)
        {
            lock (_lock)
            {
                _data.Clear();
                ScheduleWatchers(null);
            }
        }

        public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                    _executor.Enqueue(new DocumentFound(onFound, document.Version, document.Contents));
                else
                    _executor.Enqueue(onMissing);
            }
        }

        public string GetDocument(string name)
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

        public int GetVersion(string name)
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

        public void SaveDocument(string name, string value, DocumentStoreVersion expectedVersion, Action onSave, Action onConcurrency, Action<Exception> onError)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    if (!expectedVersion.VerifyVersion(document.Version))
                        _executor.Enqueue(onConcurrency);
                    else
                    {
                        document.Version++;
                        document.Contents = value;
                        _executor.Enqueue(onSave);
                        ScheduleWatchers(name);
                    }
                }
                else
                {
                    if (!expectedVersion.VerifyVersion(0))
                        _executor.Enqueue(onConcurrency);
                    else
                    {
                        _data[name] = document = new Document();
                        document.Version = 1;
                        document.Contents = value;
                        _executor.Enqueue(onSave);
                        ScheduleWatchers(name);
                    }
                }
            }
        }

        public void SaveDocument(string name, string value, int manualVersion = -1)
        {
            lock (_lock)
            {
                Document document;
                if (!_data.TryGetValue(name, out document))
                    _data[name] = document = new Document();
                document.Version = (manualVersion >= 0) ? manualVersion : document.Version + 1;
                document.Contents = value;
                ScheduleWatchers(name);
            }
        }

        public IDisposable WatchChanges(string name, Action onSomethingChanged)
        {
            lock (_lock)
            {
                var watcher = new Watcher { Document = name, OnChange = onSomethingChanged, Parent = this };
                _watchers.Add(watcher);
                return watcher;
            }
        }

        private void UnWatch(Watcher watcher)
        {
            lock (_lock)
            {
                _watchers.Remove(watcher);
            }
        }

        private void ScheduleWatchers(string document)
        {
            foreach (var watcher in _watchers)
            {
                if (watcher.Document == document || document == null)
                    _executor.Enqueue(watcher.OnChange);
            }
        }
    }
}
