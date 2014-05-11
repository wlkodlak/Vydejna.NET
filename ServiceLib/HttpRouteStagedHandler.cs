using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public class HttpRouteStagedHandler : IHttpRouteHandler
    {
        private ISerializerPicker _picker;
        private IList<IHttpSerializer> _serializers;
        private IHttpProcessor _processor;

        public HttpRouteStagedHandler(ISerializerPicker picker, IList<IHttpSerializer> serializers, IHttpProcessor processor)
        {
            _picker = picker;
            _serializers = serializers;
            _processor = processor;
        }

        public Task Handle(IHttpServerRawContext context)
        {
            return TaskUtils.FromEnumerable(HandleInternal(context)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(IHttpServerRawContext raw)
        {
            var staged = new HttpServerStagedContext(raw);
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
            
            staged.InputSerializer = _picker.PickDeserializer(staged, _serializers, null);
            staged.OutputSerializer = _picker.PickSerializer(staged, _serializers, null);
            var taskProcess = _processor.Process(staged);
            yield return taskProcess;

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
