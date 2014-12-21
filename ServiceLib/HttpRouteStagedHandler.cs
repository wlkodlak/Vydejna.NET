using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpRouteStagedHandler : IHttpRouteHandler
    {
        private readonly ISerializerPicker _picker;
        private readonly IList<IHttpSerializer> _serializers;
        private readonly IHttpProcessor _processor;
        private int _requestId;
        private static readonly HttpRouteStagedHandlerTraceSource Logger 
            = new HttpRouteStagedHandlerTraceSource("ServiceLib.HttpRouteStagedHandler");

        public HttpRouteStagedHandler(ISerializerPicker picker, IList<IHttpSerializer> serializers, IHttpProcessor processor)
        {
            _picker = picker;
            _serializers = serializers;
            _processor = processor;
        }

        public async Task Handle(IHttpServerRawContext raw, IList<RequestParameter> routeParameters)
        {
            var requestId = Interlocked.Increment(ref _requestId);
            HttpServerStagedContext staged;
            using (new LogMethod(Logger, "Handle_Input"))
            {
                staged = new HttpServerStagedContext(raw, routeParameters);
                staged.LoadParameters();

                if (raw.InputStream != null)
                {
                    var memoryStream = new MemoryStream();
                    var encoding = Encoding.UTF8;
                    var bufferCurrent = new byte[4096];
                    while (true)
                    {
                        var bytesRead = await raw.InputStream.ReadAsync(bufferCurrent, 0, 4096);
                        if (bytesRead == 0)
                            break;
                        memoryStream.Write(bufferCurrent, 0, bytesRead);
                    }
                    raw.InputStream.Dispose();
                    memoryStream.Dispose();
                    staged.InputString = encoding.GetString(memoryStream.ToArray());
                }
                else
                {
                    staged.InputString = string.Empty;
                }
                Logger.ReceivedRequest(requestId, raw, staged);

                staged.InputSerializer = _picker.PickDeserializer(staged, _serializers, null);
                staged.OutputSerializer = _picker.PickSerializer(staged, _serializers, null);
            }
            using (new LogMethod(Logger, "Handle_Process"))
            {
                await _processor.Process(staged);
            }
            using (new LogMethod(Logger, "Handle_Output"))
            {
                raw.StatusCode = staged.StatusCode;
                raw.OutputHeaders.Clear();
                foreach (var header in staged.OutputHeaders)
                    raw.OutputHeaders.Add(header.Key, header.Value);

                if (!string.IsNullOrEmpty(staged.OutputString))
                {
                    var encoding = Encoding.UTF8;
                    var buffer = encoding.GetBytes(staged.OutputString);
                    try
                    {
                        await raw.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        Logger.ResponseSent(requestId, raw, staged);
                    }
                    catch (Exception exception)
                    {
                        Logger.SendingResponseFailed(requestId, raw, staged, exception);
                    }
                    finally
                    {
                        raw.OutputStream.Dispose();
                    }
                }
            }
        }
    }

    public class HttpRouteStagedHandlerTraceSource : TraceSource
    {
        public HttpRouteStagedHandlerTraceSource(string name)
            : base(name)
        {
        }

        public void ReceivedRequest(int requestId, IHttpServerRawContext raw, HttpServerStagedContext staged)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Received request to {Url}");
            msg.SetProperty("Url", false, raw.Url);
            msg.Log(this);
        }

        public void ResponseSent(int requestId, IHttpServerRawContext raw, HttpServerStagedContext staged)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Sent response for {Url}");
            msg.SetProperty("Url", false, raw.Url);
            msg.SetProperty("StatusCode", false, staged.StatusCode);
            msg.SetProperty("Body", true, staged.OutputString);
            msg.Log(this);
        }

        public void SendingResponseFailed(int requestId, IHttpServerRawContext raw, HttpServerStagedContext staged, Exception exception)
        {
            var msg = new LogContextMessage(TraceEventType.Verbose, 1, "Failed to send response to {Url}");
            msg.SetProperty("Url", false, raw.Url);
            msg.SetProperty("Exception", true, exception);
            msg.Log(this);
        }
    }
}
