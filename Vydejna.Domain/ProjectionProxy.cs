using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public abstract class ProjectionProxy<T> : IHandle<ProjectionMetadataChanged>
         where T : class, IProjectionReader
    {
        private IProjectionMetadataManager _mgr;
        private List<ReaderInfo> _readers;
        private T _currentReader;
        private string _currentInstance;
        private string _projectionName;
        private IProjectionMetadata _metadata;
        private List<ProjectionInstanceMetadata> _instances;
        private IDisposable _changeRegistration;
        private object _lock;
        private bool _isInitialized;
        private ComparerForMetadata _comparer;
        private bool _anotherPickNeeded;
        private bool _pickInProgress;

        private class ReaderInfo
        {
            public T Reader;
            public ProjectionInstanceMetadata BestMetadata;
            public ProjectionReadability Readability;

            public ReaderInfo(T reader)
            {
                this.Reader = reader;
            }
        }

        protected T Reader
        {
            get { return _currentReader; }
        }

        protected ProjectionProxy(IProjectionMetadataManager metadataMgr, string projectionName)
        {
            _mgr = metadataMgr;
            _projectionName = projectionName;
            _readers = new List<ReaderInfo>();
            _lock = new object();
            _comparer = new ComparerForMetadata();
        }

        public void Register(T reader)
        {
            lock (_lock)
            {
                _readers.Add(new ReaderInfo(reader));
                _anotherPickNeeded = _isInitialized;
            }
            PickInstance();
        }

        public async Task InitializeInstances()
        {
            _metadata = await _mgr.GetProjection(_projectionName);
            _changeRegistration = _metadata.RegisterForChanges(null, this);
            var allInstances = await _metadata.GetAllMetadata();
            lock (_lock)
            {
                _instances = PreprocessInstances(allInstances);
                _isInitialized = true;
                _anotherPickNeeded = true;
            }
            PickInstance();
        }

        private List<ProjectionInstanceMetadata> PreprocessInstances(IEnumerable<ProjectionInstanceMetadata> allInstances)
        {
            var list = allInstances.Where(i => IsStatusAlive(i.Status)).ToList();
            list.Sort(_comparer);
            return list;
        }

        private bool IsStatusAlive(ProjectionStatus projectionStatus)
        {
            switch (projectionStatus)
            {
                case ProjectionStatus.NewBuild:
                case ProjectionStatus.Running:
                case ProjectionStatus.Legacy:
                    return true;
                default:
                    return false;
            }
        }

        public Task Handle(ProjectionMetadataChanged message)
        {
            lock (_lock)
            {
                _anotherPickNeeded = true;
                int index = _instances.BinarySearch(message.Metadata, _comparer);
                if (index < 0)
                {
                    if (IsStatusAlive(message.Status))
                        _instances.Insert(~index, message.Metadata);
                    else
                        _anotherPickNeeded = false;
                }
                else
                {
                    if (IsStatusAlive(message.Status))
                        _instances[index] = message.Metadata;
                    else
                        _instances.RemoveAt(index);
                }
                if (!_isInitialized)
                    _anotherPickNeeded = false;
            }
            PickInstance();
            return TaskResult.GetCompletedTask();
        }

        private class ComparerForMetadata : IComparer<ProjectionInstanceMetadata>
        {
            public int Compare(ProjectionInstanceMetadata x, ProjectionInstanceMetadata y)
            {
                if (ReferenceEquals(x, null))
                    return ReferenceEquals(y, null) ? 0 : 1;
                else if (ReferenceEquals(y, null))
                    return -1;
                else
                {
                    var byStatus = CompareStatus(x.Status, y.Status);
                    if (byStatus != 0)
                        return byStatus;
                    var byVersion = -string.CompareOrdinal(x.Version, y.Version);
                    if (byVersion != 0)
                        return byVersion;
                    return string.CompareOrdinal(x.Name, y.Name);
                }
            }

            private int CompareStatus(ProjectionStatus a, ProjectionStatus b)
            {
                var useA = a != ProjectionStatus.NewBuild;
                var useB = b != ProjectionStatus.NewBuild;
                if (useA)
                    return useB ? 0 : -1;
                else
                    return useB ? 1 : 0;
            }
        }

        [Flags]
        private enum PickInstanceNofification
        {
            None = 0,
            ResetOriginalInstance = 1,
            ChangeNewInstance = 2,
            RaiseReaderChange = 4
        }

        private void PickInstance()
        {
            bool performPick;
            PickInstanceNofification notifications;

            lock (_lock)
            {
                if (_pickInProgress)
                    return;
                performPick = _anotherPickNeeded && _isInitialized;
                _pickInProgress = true;
            }
            while (performPick)
            {
                T originalReader;

                lock (_lock)
                {
                    _anotherPickNeeded = false;
                    originalReader = _currentReader;

                    ReaderInfo bestReader = PickBestReader();
                    notifications = ChangeToBestReader(bestReader);
                }
                SendNotifications(originalReader, notifications);
                lock (_lock)
                    performPick = _anotherPickNeeded;
            }
            lock (_lock)
                _pickInProgress = false;
        }

        private ReaderInfo PickBestReader()
        {
            ReaderInfo bestReader = null;
            foreach (var readerInfo in _readers)
            {
                var candidates = _instances
                    .Select(i => new { Instance = i, Readability = readerInfo.Reader.GetReadability(i.MinimalReader, i.Version) })
                    .ToList();
                var bestCandidate = candidates.FirstOrDefault(c => c.Readability == ProjectionReadability.Functional) ?? candidates.FirstOrDefault();
                if (bestCandidate == null)
                {
                    readerInfo.Readability = ProjectionReadability.Unknown;
                    readerInfo.BestMetadata = null;
                }
                else
                {
                    readerInfo.Readability = bestCandidate.Readability;
                    readerInfo.BestMetadata = bestCandidate.Instance;
                    if (bestReader == null)
                        bestReader = readerInfo;
                    else if (_comparer.Compare(readerInfo.BestMetadata, bestReader.BestMetadata) < 0)
                        bestReader = readerInfo;
                }
            }
            return bestReader;
        }

        private PickInstanceNofification ChangeToBestReader(ReaderInfo bestReader)
        {
            if (bestReader == null)
            {
                if (_currentReader != null)
                {
                    _currentReader = null;
                    _currentInstance = null;
                    return PickInstanceNofification.ResetOriginalInstance | PickInstanceNofification.RaiseReaderChange;
                }
            }
            else if (bestReader == _currentReader)
            {
                var newInstance = bestReader.BestMetadata.Name;
                if (!string.Equals(_currentInstance, newInstance, StringComparison.Ordinal))
                {
                    _currentInstance = newInstance;
                    return PickInstanceNofification.ChangeNewInstance;
                }
            }
            else if (bestReader != _currentReader)
            {
                _currentReader = bestReader.Reader;
                _currentInstance = bestReader.BestMetadata.Name;
                return PickInstanceNofification.ResetOriginalInstance | PickInstanceNofification.ChangeNewInstance | PickInstanceNofification.RaiseReaderChange;
            }
            return PickInstanceNofification.None;
        }

        private void SendNotifications(T originalReader, PickInstanceNofification notifications)
        {
            if ((notifications & PickInstanceNofification.ResetOriginalInstance) != PickInstanceNofification.None)
                if (originalReader != null)
                    originalReader.UseInstance(null);
            if ((notifications & PickInstanceNofification.ChangeNewInstance) != PickInstanceNofification.None)
                _currentReader.UseInstance(_currentInstance);
            if ((notifications & PickInstanceNofification.RaiseReaderChange) != PickInstanceNofification.None)
                OnReaderChanged(originalReader);
        }

        protected virtual void OnReaderChanged(T original)
        {
        }
    }
}
