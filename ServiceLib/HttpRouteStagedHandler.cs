using log4net;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpRouteStagedHandler : IHttpRouteHandler
    {
        private ISerializerPicker _picker;
        private IList<IHttpSerializer> _serializers;
        private IHttpProcessor _processor;
        private int _requestId;
        private static readonly ILog Logger = LogManager.GetLogger("ServiceLib.HttpRouteStagedHandler");

        public HttpRouteStagedHandler(ISerializerPicker picker, IList<IHttpSerializer> serializers, IHttpProcessor processor)
        {
            _picker = picker;
            _serializers = serializers;
            _processor = processor;
        }

        public Task Handle(IHttpServerRawContext context, IList<RequestParameter> routeParameters)
        {
            return TaskUtils.FromEnumerable(HandleInternal(context, routeParameters)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(IHttpServerRawContext raw, IList<RequestParameter> routeParameters)
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
                        var taskRead = Task.Factory.FromAsync<int>(raw.InputStream.BeginRead(bufferCurrent, 0, 4096, null, null), raw.InputStream.EndRead);
                        yield return taskRead;
                        var bytesRead = taskRead.Result;
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
                Logger.DebugFormat("[{0}] Received request {1} {2}, postdata: {3}",
                    requestId, raw.Method, raw.Url, staged.InputString);

                staged.InputSerializer = _picker.PickDeserializer(staged, _serializers, null);
                staged.OutputSerializer = _picker.PickSerializer(staged, _serializers, null);
            }
            using (new LogMethod(Logger, "Handle_Process"))
            {
                var taskProcess = _processor.Process(staged);
                yield return taskProcess;
                taskProcess.Wait();
            }
            using (new LogMethod(Logger, "Handle_Output"))
            {
                Logger.DebugFormat("[{0}] Sending response {1} to {2}: {3}",
                    requestId, staged.StatusCode, raw.Url, staged.OutputString);

                raw.StatusCode = staged.StatusCode;
                raw.OutputHeaders.Clear();
                foreach (var header in staged.OutputHeaders)
                    raw.OutputHeaders.Add(header.Key, header.Value);

                if (!string.IsNullOrEmpty(staged.OutputString))
                {
                    var encoding = Encoding.UTF8;
                    var buffer = encoding.GetBytes(staged.OutputString);
                    var taskWrite = Task.Factory.FromAsync(raw.OutputStream.BeginWrite(buffer, 0, buffer.Length, null, null), raw.OutputStream.EndWrite);
                    yield return taskWrite;
                    if (taskWrite.Exception != null)
                        taskWrite.Exception.Handle(ex => true);
                    raw.OutputStream.Dispose();
                }
            }
        }
    }
}
