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
        private List<string> _log;

        private class Watcher : IDisposable
        {
            public TestDocumentFolder Parent;
            public string Document;
            public int SinceVersion;
            public Action OnChange;
            public void Dispose() { Parent.UnWatch(this); }
        }
        private class Document
        {
            public int Version;
            public string Contents;
            public override string ToString()
            {
                return string.Format("@{0}: {1}", Version, Contents);
            }
        }

        public TestDocumentFolder(IQueueExecution executor)
        {
            _executor = executor;
            _lock = new object();
            _data = new Dictionary<string, Document>();
            _watchers = new List<Watcher>();
            _log = new List<string>();
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
                AddToLog("Clear", null, 0);
                ScheduleWatchers(null, 0);
                _executor.Enqueue(onComplete);
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

        public void GetDocument(string name, Action<int, string> onFound, Action onMissing, Action<Exception> onError)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    AddToLog("Get", name, document.Version);
                    _executor.Enqueue(new DocumentFound(onFound, document.Version, document.Contents));
                }
                else
                {
                    AddToLog("Get", name, 0);
                    _executor.Enqueue(onMissing);
                }
            }
        }

        public void GetNewerDocument(string name, int knownVersion, Action<int, string> onFoundNewer, Action onNotModified, Action onMissing, Action<Exception> onError)
        {
            lock (_lock)
            {
                Document document;
                if (_data.TryGetValue(name, out document))
                {
                    AddToLog("Get", name, document.Version);
                    if (document.Version == knownVersion)
                        _executor.Enqueue(onNotModified);
                    else
                        _executor.Enqueue(new DocumentFound(onFoundNewer, document.Version, document.Contents));
                }
                else
                {
                    AddToLog("Get", name, 0);
                    _executor.Enqueue(onMissing);
                }
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
                    {
                        AddToLog("Conflict", name, document.Version);
                        _executor.Enqueue(onConcurrency);
                    }
                    else
                    {
                        document.Version++;
                        document.Contents = value;
                        AddToLog("Save", name, document.Version);
                        _executor.Enqueue(onSave);
                        ScheduleWatchers(name, document.Version);
                    }
                }
                else
                {
                    if (!expectedVersion.VerifyVersion(0))
                    {
                        AddToLog("Conflict", name, 0);
                        _executor.Enqueue(onConcurrency);
                    }
                    else
                    {
                        _data[name] = document = new Document();
                        document.Version = 1;
                        document.Contents = value;
                        AddToLog("Save", name, 1);
                        _executor.Enqueue(onSave);
                        ScheduleWatchers(name, document.Version);
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
                ScheduleWatchers(name, document.Version);
            }
        }

        public IDisposable WatchChanges(string name, int sinceVersion, Action onSomethingChanged)
        {
            lock (_lock)
            {
                Document document;
                int currentVersion;
                var watcher = new Watcher { Document = name, SinceVersion = sinceVersion, OnChange = onSomethingChanged, Parent = this };
                _watchers.Add(watcher);
                if (_data.TryGetValue(name, out document))
                    currentVersion = document.Version;
                else
                    currentVersion = 0;
                if (currentVersion != sinceVersion)
                    _executor.Enqueue(onSomethingChanged);
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

        private void ScheduleWatchers(string document, int version)
        {
            foreach (var watcher in _watchers)
            {
                if (watcher.Document == document || document == null)
                {
                    if (watcher.SinceVersion != version)
                        _executor.Enqueue(watcher.OnChange);
                }
            }
        }
    }
}
