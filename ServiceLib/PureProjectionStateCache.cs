using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IPureProjectionStateCache<TState>
    {
        void Get(string partition, Action<TState> onCompleted, Action<Exception> onError);
        void Set(string partition, TState state, Action onCompleted, Action<Exception> onError);
        void Reset(string version, Action onCompleted, Action<Exception> onError);
        void Flush(Action onCompleted, Action<Exception> onError);
        void LoadMetadata(Action<string, EventStoreToken> onCompleted, Action<Exception> onError);
        void SetVersion(string version);
        void SetToken(EventStoreToken token);
        void SetupFlushing(bool enabled, bool allowWhenRebuilding, int counter);
    }

    public class PureProjectionStateCache<TState> : IPureProjectionStateCache<TState>
    {
        private class CacheItem
        {
            public string Partition;
            public TState State;
            public bool Dirty;
            public bool Touched;
            public bool Loaded;
            public int Age;
        }

        private Dictionary<string, CacheItem> _index;
        private IDocumentFolder _store;
        private IPureProjectionSerializer<TState> _serializer;

        private bool _metadataLoaded, _tokenDirty, _versionDirty;
        private string _version;
        private EventStoreToken _token;
        private bool _autoFlushEnabled, _autoFlushWhenRebuilding;
        private int _autoFlushInitial, _autoFlushLeft;
        private bool _deleteAll;

        public PureProjectionStateCache(IDocumentFolder store, IPureProjectionSerializer<TState> serializer)
        {
            _store = store;
            _serializer = serializer;
            _index = new Dictionary<string, CacheItem>();
            _autoFlushEnabled = true;
            _autoFlushInitial = _autoFlushLeft = 100;
        }

        public void SetupFlushing(bool enabled, bool allowWhenRebuilding, int counter)
        {
            _autoFlushEnabled = enabled;
            _autoFlushWhenRebuilding = allowWhenRebuilding;
            _autoFlushInitial = _autoFlushLeft = counter;
        }

        private CacheItem FindOrCreate(string partition)
        {
            CacheItem item;
            if (!_index.TryGetValue(partition, out item))
            {
                item = new CacheItem();
                item.Partition = partition;
                _index[partition] = item;
            }
            item.Touched = true;
            return item;
        }

        private void MetadataLoaded(string version, EventStoreToken token)
        {
            _metadataLoaded = true;
            _version = version;
            _token = token;
        }

        private void EjectOldCacheValues()
        {
            var keysForRemoving = new List<string>();
            foreach (var cacheItem in _index.Values)
            {
                if (cacheItem.Touched)
                    cacheItem.Age = 0;
                else if (cacheItem.Age < 2)
                    cacheItem.Age++;
                else if (!cacheItem.Dirty)
                    keysForRemoving.Add(cacheItem.Partition);
                cacheItem.Touched = false;
            }
            foreach (var partition in keysForRemoving)
                _index.Remove(partition);
        }

        public void Get(string partition, Action<TState> onCompleted, Action<Exception> onError)
        {
            var item = FindOrCreate(partition);
            if (item.Loaded)
                onCompleted(item.State);
            else if (_deleteAll)
            {
                item.State = _serializer.InitialState();
                item.Loaded = true;
            }
            else
                new LoadPartitionWorker(this, item, onCompleted, onError).Execute();
        }

        private class LoadPartitionWorker
        {
            private PureProjectionStateCache<TState> _parent;
            private CacheItem _item;
            private Action<TState> _onCompleted;
            private Action<Exception> _onError;

            public LoadPartitionWorker(PureProjectionStateCache<TState> parent, CacheItem item, Action<TState> onCompleted, Action<Exception> onError)
            {
                _parent = parent;
                _item = item;
                _onCompleted = onCompleted;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._store.GetDocument(_item.Partition, OnPartitionLoaded, OnPartitionMissing, _onError);
            }
            private void OnPartitionLoaded(int version, string contents)
            {
                try
                {
                    _item.State = _parent._serializer.Deserialize(contents);
                    _onCompleted(_item.State);
                }
                catch (Exception exception)
                {
                    _onError(exception);
                }
            }
            private void OnPartitionMissing()
            {
                try
                {
                    _item.State = _parent._serializer.InitialState();
                    _onCompleted(_item.State);
                }
                catch (Exception exception)
                {
                    _onError(exception);
                }
            }
        }

        public void Set(string partition, TState state, Action onCompleted, Action<Exception> onError)
        {
            var item = FindOrCreate(partition);
            item.State = state;
            item.Dirty = true;
            item.Touched = true;
            item.Loaded = true;
            AutoFlush(onCompleted, onError);
        }

        public void Reset(string version, Action onCompleted, Action<Exception> onError)
        {
            _index.Clear();
            _deleteAll = true;
            _version = version;
            _token = EventStoreToken.Initial;
            _versionDirty = _tokenDirty = true;
            AutoFlush(onCompleted, onError);
        }

        public void Flush(Action onCompleted, Action<Exception> onError)
        {
            _autoFlushLeft = _autoFlushInitial;
            new FlushWorker(this, onCompleted, onError).Execute();
        }

        private void AutoFlush(Action onCompleted, Action<Exception> onError)
        {
            _autoFlushLeft--;
            if (_autoFlushLeft > 0)
                onCompleted();
            else
            {
                _autoFlushLeft = _autoFlushInitial;
                bool peformFlush = _autoFlushEnabled && (_deleteAll || _autoFlushWhenRebuilding);
                if (peformFlush)
                    new FlushWorker(this, onCompleted, onError).Execute();
                else
                {
                    EjectOldCacheValues();
                    onCompleted();
                }
            }
        }

        private class FlushWorker
        {
            private PureProjectionStateCache<TState> _parent;
            private Action _onCompleted;
            private Action<Exception> _onError;
            private int _position;
            private IList<CacheItem> _dirtyItems;
            private CacheItem _currentDirtyItem;

            public FlushWorker(PureProjectionStateCache<TState> parent, Action onCompleted, Action<Exception> onError)
            {
                _parent = parent;
                _onCompleted = onCompleted;
                _onError = onError;
                _position = -1;
                _dirtyItems = new List<CacheItem>(parent._index.Count);
            }

            public void Execute()
            {
                foreach (var cacheItem in _parent._index.Values)
                {
                    if (cacheItem.Dirty)
                        _dirtyItems.Add(cacheItem);
                }
                if (_parent._deleteAll)
                {
                    _parent._deleteAll = false;
                    _parent._store.DeleteAll(ProcessNextDirtyItem, _onError);
                }
                else
                    ProcessNextDirtyItem();
            }
            private CacheItem GetNextDirtyItem()
            {
                _position++;
                while (_position < _dirtyItems.Count)
                {
                    var item = _dirtyItems[_position];
                    if (item.Dirty)
                        return item;
                    _position++;
                }
                return null;
            }
            private void ProcessNextDirtyItem()
            {
                if (_currentDirtyItem != null)
                    _currentDirtyItem.Dirty = false;
                _currentDirtyItem = GetNextDirtyItem();
                if (_currentDirtyItem != null)
                {
                    var serialized = _parent._serializer.Serialize(_currentDirtyItem.State);
                    _parent._store.SaveDocument(_currentDirtyItem.Partition, serialized, DocumentStoreVersion.Any, ProcessNextDirtyItem, ProcessNextDirtyItem, _onError);
                }
                else
                    SaveVersion();
            }
            private void SaveVersion()
            {
                if (_parent._versionDirty)
                    _parent._store.SaveDocument("__version", _parent._version, DocumentStoreVersion.Any, SaveToken, SaveToken, _onError);
                else
                    SaveToken();
            }
            private void SaveToken()
            {
                if (_parent._tokenDirty)
                    _parent._store.SaveDocument("__token", _parent._token.ToString(), DocumentStoreVersion.Any, ReportComplete, ReportComplete, _onError);
                else
                    ReportComplete();
            }
            private void ReportComplete()
            {
                _parent._versionDirty = false;
                _parent._tokenDirty = false;
                _parent.EjectOldCacheValues();
                _onCompleted();
            }
        }

        public void LoadMetadata(Action<string, EventStoreToken> onCompleted, Action<Exception> onError)
        {
            if (_metadataLoaded)
                onCompleted(_version, _token);
            else
                new LoadMetadataWorker(this, onCompleted, onError).Execute();
        }

        private class LoadMetadataWorker
        {
            private PureProjectionStateCache<TState> _parent;
            private Action<string, EventStoreToken> _onCompleted;
            private Action<Exception> _onError;
            private string _version;
            private EventStoreToken _token;

            public LoadMetadataWorker(PureProjectionStateCache<TState> parent, Action<string, EventStoreToken> onCompleted, Action<Exception> onError)
            {
                _parent = parent;
                _onCompleted = onCompleted;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._store.GetDocument("__version", VersionLoaded, LoadToken, _onError);
            }
            private void VersionLoaded(int docVersion, string docValue)
            {
                _version = docValue;
                LoadToken();
            }
            private void LoadToken()
            {
                _parent._store.GetDocument("__token", TokenLoaded, ReportResult, _onError);
            }
            private void TokenLoaded(int docVersion, string docValue)
            {
                _token = new EventStoreToken(docValue);
                ReportResult();
            }
            private void ReportResult()
            {
                _parent.MetadataLoaded(_version, _token);
                _onCompleted(_version, _token);
            }
        }

        public void SetVersion(string version)
        {
            if (!_metadataLoaded)
            {
                _metadataLoaded = true;
                _token = EventStoreToken.Initial;
            }
            _version = version;
            _versionDirty = true;
        }

        public void SetToken(EventStoreToken token)
        {
            _metadataLoaded = true;
            _token = token;
            _tokenDirty = true;
        }
    }
}
