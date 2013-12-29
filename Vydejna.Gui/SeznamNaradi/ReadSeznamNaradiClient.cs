using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Gui.SeznamNaradi
{
    public class ReadSeznamNaradiClient : IReadSeznamNaradi
    {
        private string _url;
        private IHttpClient _client;

        public ReadSeznamNaradiClient(string url, IHttpClient client)
        {
            this._url = url;
            this._client = client;
        }

        public void Handle(QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> message)
        {
            new ZiskatSeznamNaradiWorker(this, message).Execute();
        }

        public void Handle(QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> message)
        {
            new OvereniUnikatnostiWorker(this, message).Execute();
        }

        private class ZiskatSeznamNaradiWorker
        {
            private ReadSeznamNaradiClient _parent;
            private QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> _message;
            private RestClient _restCall;

            public ZiskatSeznamNaradiWorker(ReadSeznamNaradiClient parent, QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> message)
            {
                this._parent = parent;
                this._message = message;
            }

            public void Execute()
            {
                try
                {
                    _restCall = new RestClientJson(_parent._url + "SeznamNaradi", _parent._client)
                        .AddParameter("offset", _message.Request.Offset.ToString())
                        .AddParameter("pocet", _message.Request.MaxPocet.ToString());
                    _restCall.Execute(OnExecuted, OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }

            private void OnError(Exception ex)
            {
                _message.OnError(ex);
            }

            private void OnExecuted(RestClientResult result)
            {
                try
                {
                    var payload = result.GetPayload<ZiskatSeznamNaradiResponse>();
                    _message.OnCompleted(payload);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
        }

        private class OvereniUnikatnostiWorker
        {
            private ReadSeznamNaradiClient _parent;
            private QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> _message;
            private RestClient _restCall;

            public OvereniUnikatnostiWorker(ReadSeznamNaradiClient parent, QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> message)
            {
                this._parent = parent;
                this._message = message;
            }

            public void Execute()
            {
                try
                {
                    _restCall = new RestClientJson(_parent._url + "OveritUnikatnost", _parent._client)
                        .AddParameter("vykres", _message.Request.Vykres)
                        .AddParameter("rozmer", _message.Request.Rozmer);
                    _restCall.Execute(OnExecuted, OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }

            private void OnError(Exception ex)
            {
                _message.OnError(ex);
            }

            private void OnExecuted(RestClientResult result)
            {
                try
                {
                    var payload = result.GetPayload<OvereniUnikatnostiResponse>();
                    _message.OnCompleted(payload);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
        }
    }
}
