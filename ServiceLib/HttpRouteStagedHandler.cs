using System.Collections.Generic;
using System.IO;
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
            this._picker = picker;
            this._serializers = serializers;
            this._processor = processor;
        }

        public void Handle(IHttpServerRawContext context)
        {
            new Worker(this, context).StartExecuting();
        }

        private class Worker
        {
            private HttpRouteStagedHandler _parent;
            private IHttpServerRawContext _rawContext;
            private HttpServerStagedContext _staged;
            private StreamReader _inputReader;

            public Worker(HttpRouteStagedHandler parent, IHttpServerRawContext context)
            {
                _parent = parent;
                _rawContext = context;
                _staged = new HttpServerStagedContext(_rawContext);
            }
            public void StartExecuting()
            {
                try
                {
                    _staged.LoadParameters();
                    if (_rawContext.InputStream != null)
                    {
                        _inputReader = new StreamReader(_rawContext.InputStream);
                        _inputReader.ReadToEndAsync().ContinueWith(OnInputReadCompleted);
                    }
                    else
                    {
                        _staged.InputString = string.Empty;
                        CallProcessor();
                    }
                }
                catch
                {
                    _rawContext.StatusCode = 500;
                    _rawContext.Close();
                }
            }
            private void OnInputReadCompleted(Task<string> inputStringTask)
            {
                try
                {
                    try
                    {
                        _staged.InputString = inputStringTask.Result;
                    }
                    finally
                    {
                        _inputReader.Dispose();
                    }
                    CallProcessor();
                }
                catch
                {
                    _rawContext.StatusCode = 500;
                    _rawContext.Close();
                }

            }
            private void CallProcessor()
            {
                _staged.InputSerializer = _parent._picker.PickDeserializer(_staged, _parent._serializers, null);
                _staged.OutputSerializer = _parent._picker.PickSerializer(_staged, _parent._serializers, null);
                _parent._processor.StartProcessing(_staged);
            }
        }
    }
}
