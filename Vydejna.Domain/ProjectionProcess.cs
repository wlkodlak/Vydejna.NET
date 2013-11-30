using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IProjectionProcessHandlerCollection
    {
        IHandleRegistration<T> Register<T>(IHandle<T> handler);
        void Handle(Type type, object evt);
        ICollection<Type> HandledTypes();
    }

    public class ProjectionProcessHandlers : IProjectionProcessHandlerCollection
    {
        private Dictionary<Type, Action<object>> _handlers;
        
        public ProjectionProcessHandlers()
        {
            _handlers = new Dictionary<Type, Action<object>>();
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            _handlers[typeof(T)] = CreateDispatchableHandler(handler);
            return new Registration<T>(this);
        }

        private static Action<object> CreateDispatchableHandler<T>(IHandle<T> handler)
        {
            // return o => handler.Handle((T)o);
            var param = Expression.Parameter(typeof(object), "o");
            var cast = Expression.Convert(param, typeof(T));
            var handleMethod = typeof(IHandle<T>).GetMethod("Handle");
            var invoke = Expression.Call(Expression.Constant(handler), handleMethod, cast);
            var name = "Handle_" + typeof(T).Name;
            return Expression.Lambda<Action<object>>(invoke, name, new[] { param }).Compile();
        }

        private class Registration<T> : IHandleRegistration<T>
        {
            private ProjectionProcessHandlers _process;
            public Registration(ProjectionProcessHandlers process)
            {
                _process = process;
            }

            public void ReplaceWith(IHandle<T> handler)
            {
                _process.Register(handler);
            }
        }

        public void Handle(Type type, object evt)
        {
            Action<object> handler;
            if (evt == null)
                return;
            if (type == null)
                type = evt.GetType();
            if (_handlers.TryGetValue(type, out handler))
                handler(evt);
        }

        public ICollection<Type> HandledTypes()
        {
            return _handlers.Keys;
        }
    }

    public class ProjectionProcess
    {
        private string _instanceName;
        private IEventStreaming _streamer;
        private IProjectionMetadataManager _metadataManager;
        private IProjectionMetadata _metadata;
        private IProjection _projection;
        private IProjectionProcessHandlerCollection _handlers;
        private IEventSourcedSerializer _serializer;
        private EventStoreToken _currentToken;
        private IEventStreamingInstance _openedEventStream;
        private CancellationTokenSource _cancel;
        private bool _isRebuilder;

        public ProjectionProcess(IEventStreaming streamer, IProjectionMetadataManager metadataManager, IEventSourcedSerializer serializer)
        {
            _streamer = streamer;
            _metadataManager = metadataManager;
            _serializer = serializer;
            _handlers = new ProjectionProcessHandlers();
            _cancel = new CancellationTokenSource();
        }

        public ProjectionProcess Setup(IProjection projection)
        {
            _projection = projection;
            return this;
        }

        public ProjectionProcess AsMaster()
        {
            return this;
        }

        public ProjectionProcess AsRebuilder()
        {
            _isRebuilder = true;
            return this;
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            return _handlers.Register(handler);
        }

        public async Task Start()
        {
            if (_isRebuilder)
                return;

            _metadata = await _metadataManager.GetProjection(_projection.GetConsumerName());
            var allMetadata = await _metadata.GetAllMetadata();

            var metadata = allMetadata.FirstOrDefault(m => m.Status == ProjectionStatus.Running);
            metadata = metadata ?? allMetadata.FirstOrDefault(m => m.Status == ProjectionStatus.NewBuild);

            if (metadata == null)
            {
                _instanceName = _projection.GenerateInstanceName(null);
                _currentToken = EventStoreToken.Initial;
                await _metadata.BuildNewInstance(_instanceName, null, _projection.GetVersion(), _projection.GetMinimalReader());
                await _projection.StartRebuild(false);
            }
            else
            {
                _instanceName = metadata.Name;
                _currentToken = await _metadata.GetToken(_instanceName);
                await _metadata.BuildNewInstance(_instanceName, null, _projection.GetVersion(), _projection.GetMinimalReader());
                await _projection.StartRebuild(true);
            }

            _openedEventStream = _streamer.GetStreamer(_handlers.HandledTypes(), _currentToken, true);

            while (!_cancel.IsCancellationRequested)
            {
                try
                {
                    var storedEvent = await _openedEventStream.GetNextEvent(_cancel.Token);
                    if (storedEvent == null)
                    {
                        await _projection.CommitRebuild();
                        await _metadata.UpdateStatus(_instanceName, ProjectionStatus.Running);
                        await _metadata.SetToken(_instanceName, _currentToken);
                    }
                    else
                    {
                        var objectEvent = _serializer.Deserialize(storedEvent);
                        _handlers.Handle(null, objectEvent);
                        _currentToken = storedEvent.Token;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            await _metadata.SetToken(_instanceName, _currentToken);
        }

        public void Stop()
        {
            _cancel.Cancel();
        }
    }
}
