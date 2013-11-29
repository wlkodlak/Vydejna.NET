using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
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

        public ProjectionProcess(IEventStreaming streamer, IProjectionMetadataManager metadataManager, IEventSourcedSerializer serializer)
        {
            _streamer = streamer;
            _metadataManager = metadataManager;
            _serializer = serializer;
            _handlers = new ProjectionProcessHandlers();
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

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            return _handlers.Register(handler);
        }

        public async Task Initialize()
        {
            _metadata = await _metadataManager.GetProjection(_projection.GetConsumerName());
            var allMetadata = await _metadata.GetAllMetadata();
            _instanceName = _projection.GenerateInstanceName(null);
            await _metadata.BuildNewInstance(_instanceName, null, _projection.GetVersion(), _projection.GetMinimalReader());
            await _projection.StartRebuild(false);

            _currentToken = EventStoreToken.Initial;
            _openedEventStream = _streamer.GetStreamer(_handlers.HandledTypes(), _currentToken, true);
        }

        public async Task Step()
        {
            var storedEvent = await _openedEventStream.GetNextEvent();
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
    }
}
