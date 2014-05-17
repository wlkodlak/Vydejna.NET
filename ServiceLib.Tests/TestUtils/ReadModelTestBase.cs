using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServiceLib;
using ServiceLib.Tests.TestUtils;
using System;
using System.Threading;

namespace ServiceLib.Tests.TestUtils
{
    public abstract class ReadModelTestBase
    {
        protected TestScheduler _scheduler;
        protected VirtualTime _time;
        protected TestDocumentFolder _folder;
        protected IEventProjection _projection;
        protected object _reader;
        private bool _inRebuildMode, _isStarted, _isFlushed;

        public DateTime CurrentTime
        {
            get { return _time.GetUtcTime(); }
            set { _time.SetTime(value); }
        }

        [TestInitialize]
        public void Initialize()
        {
            _scheduler = new TestScheduler();
            _time = new VirtualTime();
            _time.SetTime(new DateTime(2014, 2, 18, 11, 18, 22));
            _folder = new TestDocumentFolder();
            InitializeCore();
            _projection = CreateProjection();
            _reader = CreateReader();
            Given();
            When();
        }

        protected abstract IEventProjection CreateProjection();
        protected abstract object CreateReader();
        protected virtual void InitializeCore()
        {
        }
        protected virtual void Given()
        {
        }
        protected virtual void When()
        {
        }

        public TResponse ReadProjection<TRequest, TResponse>(TRequest request)
            where TRequest : class
            where TResponse : class
        {
            if (_isStarted && !_isFlushed)
                Flush();

            var task = _scheduler.Run(() => ((IAnswer<TRequest, TResponse>)_reader).Handle(request));
            var response = task.Result;
            Assert.IsNotNull(response, "Query response NULL");
            return response;
        }

        public void StartRebuild(string storedVersion = null)
        {
            _inRebuildMode = true;
            var mode = _projection.UpgradeMode(storedVersion);
            Assert.AreEqual(EventProjectionUpgradeMode.Rebuild, mode, "Expected rebuild");
            SendEventInternal(new ProjectorMessages.Reset(), true);
            _isStarted = true;
            _isFlushed = true;
        }

        public void Resume(string storedVersion)
        {
            var mode = _projection.UpgradeMode(storedVersion);
            Assert.AreEqual(EventProjectionUpgradeMode.NotNeeded, mode, "Rebuild unexpected");
            SendEventInternal(new ProjectorMessages.Resume(), false);
            _isStarted = true;
            _isFlushed = true;
        }

        public void Flush()
        {
            if (!_isStarted)
                StartRebuild(null);
            if (_inRebuildMode)
            {
                _inRebuildMode = false;
                SendEventInternal(new ProjectorMessages.RebuildFinished(), false);
            }
            SendEventInternal(new ProjectorMessages.Flush(), false);
            _isFlushed = true;
        }

        public void SendEvent<T>(T evnt)
        {
            if (!_isStarted)
                StartRebuild(null);
            _isFlushed = false;
            SendEventInternal(evnt, true);
        }

        private void SendEventInternal<T>(T evnt, bool mustImplement)
        {
            var handler = _projection as IProcess<T>;
            if (handler == null)
            {
                if (mustImplement)
                    throw new InvalidCastException(string.Format("Event type {0} is not supported", typeof(T).Name));
                else
                    return;
            }
            var task =_scheduler.Run(() => handler.Handle(evnt));
            Assert.IsTrue(task.Wait(500), "Query didn't finish in time");
        }
    }
}
