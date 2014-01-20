using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace ServiceLib
{
    public interface INotifyChange
    {
        void Notify(string partition, int version);
        IDisposable Register(Action<string, int> onNotify);
    }

    public class NotifyChangeDirect : INotifyChange
    {
        private IQueueExecution _executor;
        private int _key = 0;
        private ConcurrentDictionary<int, Registration> _watchers;

        public NotifyChangeDirect(IQueueExecution executor)
        {
            _executor = executor;
            _watchers = new ConcurrentDictionary<int, Registration>();
        }

        public void Notify(string partition, int version)
        {
            foreach (var watcher in _watchers)
                watcher.Value.Notify(partition, version);
        }

        public IDisposable Register(Action<string, int> onNotify)
        {
            var key = Interlocked.Increment(ref _key);
            var registration = new Registration(this, onNotify, key);
            _watchers[key] = registration;
            return registration;
        }

        private void Unregister(int key)
        {
            Registration removed;
            _watchers.TryRemove(key, out removed);
        }

        private class Registration : IDisposable
        {
            private bool _enabled;
            private Action<string, int> _onNotify;
            private NotifyChangeDirect _parent;
            private int _key;

            public Registration(NotifyChangeDirect parent, Action<string, int> onNotify, int key)
            {
                _parent = parent;
                _onNotify = onNotify;
                _enabled = true;
                _key = key;
            }

            public void Notify(string partition, int version)
            {
                if (_enabled)
                    _parent._executor.Enqueue(new NotifyChangesDispatcher(_onNotify, partition, version));
            }

            public void Dispose()
            {
                _enabled = false;
                _parent.Unregister(_key);
            }
        }
    }

    public class NotifyChangesDispatcher : IQueuedExecutionDispatcher
    {
        private string _partition;
        private int _version;
        private Action<string, int> _onNotify;

        public NotifyChangesDispatcher(Action<string, int> onNotify, string partition, int version)
        {
            _onNotify = onNotify;
            _partition = partition;
            _version = version;
        }

        public void Execute()
        {
            _onNotify(_partition, _version);
        }
    }

    public class NotifyChangePostgres : INotifyChange
    {
        private IQueueExecution _executor;
        private int _key;
        private int _isListening;
        private ConcurrentDictionary<int, Registration> _watchers;
        private DatabasePostgres _db;
        private string _notificationName;

        public NotifyChangePostgres(DatabasePostgres db, IQueueExecution executor, string notificationName)
        {
            _db = db;
            _executor = executor;
            _notificationName = notificationName;
            _watchers = new ConcurrentDictionary<int, Registration>();
        }

        public void Notify(string partition, int version)
        {
            _db.Notify(_notificationName, string.Format("{0}:{1}", version, partition));
        }

        private char[] separators = new[] { ':' };

        private void OnNotify(string payload)
        {
            string partition = "";
            int version = 0;
            if (!string.IsNullOrEmpty(payload))
            {
                var parts = payload.Split(separators, 2);
                if (parts.Length >= 1)
                    partition = parts[0];
                if (parts.Length >= 2)
                    int.TryParse(parts[1], out version);

            }
            foreach (var watcher in _watchers)
                watcher.Value.Notify(partition, version);
        }

        public IDisposable Register(Action<string, int> onNotify)
        {
            if (Interlocked.CompareExchange(ref _isListening, 1, 0) == 0)
                _db.Listen(_notificationName, OnNotify);
            var key = Interlocked.Increment(ref _key);
            var registration = new Registration(this, onNotify, key);
            _watchers[key] = registration;
            return registration;
        }

        private void Unregister(int key)
        {
            Registration removed;
            _watchers.TryRemove(key, out removed);
        }

        private class Registration : IDisposable
        {
            private bool _enabled;
            private Action<string, int> _onNotify;
            private NotifyChangePostgres _parent;
            private int _key;

            public Registration(NotifyChangePostgres parent, Action<string, int> onNotify, int key)
            {
                _parent = parent;
                _onNotify = onNotify;
                _enabled = true;
                _key = key;
            }

            public void Notify(string partition, int version)
            {
                if (_enabled)
                    _onNotify(partition, version);
            }

            public void Dispose()
            {
                _enabled = false;
                _parent.Unregister(_key);
            }
        }
    }
}
