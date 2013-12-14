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

        public EventProcess(IEventStreaming streamer, IProjectionMetadataManager metadataManager, IEventSourcedSerializer serializer)
        {
            _streamer = streamer;
            _metadataMgr = metadataManager;
            _serializer = serializer;
            _subscriptions = new SubscriptionManager();
        }

        public EventProcess Setup(IEventConsumer consumer)
        {
            _consumer = consumer;
            return this;
        }

        public IHandleRegistration<T> Register<T>(IHandle<T> handler)
        {
            return _subscriptions.Register(handler);
        }

        public async Task Start()
        {
            _cancel = new CancellationTokenSource();
            _metadata = await _metadataMgr.GetHandler(_consumer.GetConsumerName()).ConfigureAwait(false);
            _token = _metadata.GetToken() ?? EventStoreToken.Initial;
            _stream = _streamer.GetStreamer(_subscriptions.GetHandledTypes(), _token, false);
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var storedEvent = await _stream.GetNextEvent(_cancel.Token).ConfigureAwait(false);
                    var objectEvent = _serializer.Deserialize(storedEvent);
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
                            handler.HandleError(objectEvent, exception);
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            await _consumer.HandleShutdown().ConfigureAwait(false);
        }

        public void Stop()
        {
            _cancel.Cancel();
            _cancel.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }

    }
}
