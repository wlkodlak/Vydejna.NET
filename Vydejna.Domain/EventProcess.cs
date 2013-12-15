using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class EventProcess : IDisposable
    {
        private IEventStreaming _streamer;
        private IProjectionMetadataManager _metadataMgr;
        private IEventSourcedSerializer _serializer;
        private IEventConsumer _consumer;
        private ISubscriptionManager _subscriptions;
        private CancellationTokenSource _cancel;
        private IEventsConsumerMetadata _metadata;
        private IEventStreamingInstance _stream;
        private EventStoreToken _token;
        private Task _processTask;
        private log4net.ILog _log;

        public EventProcess(IEventStreaming streamer, IProjectionMetadataManager metadataManager, IEventSourcedSerializer serializer)
        {
            _streamer = streamer;
            _metadataMgr = metadataManager;
            _serializer = serializer;
            _subscriptions = new SubscriptionManager();
            _log = log4net.LogManager.GetLogger("EventProcess.Default");
        }

        public EventProcess Setup(IEventConsumer consumer)
        {
            _consumer = consumer;
            _log = log4net.LogManager.GetLogger(string.Format("EventProcess.{0}", consumer.GetConsumerName()));
            _log.Debug("Setup");
            return this;
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            _log.DebugFormat("Register({0})", typeof(T).Name);
            return _subscriptions.Register(handler);
        }

        public void Start()
        {
            _processTask = Core();
        }

        public async Task Core()
        {
            _log.Info("Event processor started");
            _cancel = new CancellationTokenSource();
            _metadata = await _metadataMgr.GetHandler(_consumer.GetConsumerName()).ConfigureAwait(false);
            _token = _metadata.GetToken() ?? EventStoreToken.Initial;
            _log.DebugFormat("Starting at token {0}", _token);
            _stream = _streamer.GetStreamer(_subscriptions.GetHandledTypes(), _token, false);
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var storedEvent = await _stream.GetNextEvent(_cancel.Token).ConfigureAwait(false);
                    var objectEvent = _serializer.Deserialize(storedEvent);
                    _log.DebugFormat("Processing message {0} (token {1})", storedEvent.Type, storedEvent.Token);
                    var handlers = _subscriptions.FindHandlers(objectEvent.GetType());
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler.Handle(objectEvent).ConfigureAwait(false);
                            await _metadata.SetToken(storedEvent.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            _log.WarnFormat("Message {0} (token {1}) caused exception {2}", storedEvent.Type, storedEvent.Token, exception);
                            handler.HandleError(objectEvent, exception);
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            await _consumer.HandleShutdown().ConfigureAwait(false);
            _log.Info("Event processor ended");
        }

        public void Stop()
        {
            _cancel.Cancel();
            _processTask.Wait();
            _cancel.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }

    }
}
