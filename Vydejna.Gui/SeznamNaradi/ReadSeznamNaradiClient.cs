using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vydejna.Contracts;
using System.Threading.Tasks;

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

        public async Task<OvereniUnikatnostiResponse> Handle(OvereniUnikatnostiRequest request)
        {
            var json = new RestClientJson(_url + "OveritUnikatnost", _client)
                .AddParameter("vykres", request.Vykres)
                .AddParameter("rozmer", request.Rozmer);
            var result = await json.Execute().ConfigureAwait(false);
            return result.GetPayload<OvereniUnikatnostiResponse>();
        }

        public void Handle(QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> message)
        {
            new ZiskatSeznamNaradiWorker(this, message).Execute();
        }

        public void Handle(QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> message)
        {
            throw new NotImplementedException();
        }

        private class ZiskatSeznamNaradiWorker
        {
            private ReadSeznamNaradiClient _parent;
            private QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> _message;

            public ZiskatSeznamNaradiWorker(ReadSeznamNaradiClient parent, QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> message)
            {
                this._parent = parent;
                this._message = message;
            }


            public void Execute()
            {
                var json = new RestClientJson(_parent._url + "SeznamNaradi", _parent._client)
                    .AddParameter("offset", _message.Request.Offset.ToString())
                    .AddParameter("pocet", _message.Request.MaxPocet.ToString());
                var result = json.Execute(OnExecuted, OnError);
                return result.GetPayload<ZiskatSeznamNaradiResponse>();
            }
        }
    }
}
