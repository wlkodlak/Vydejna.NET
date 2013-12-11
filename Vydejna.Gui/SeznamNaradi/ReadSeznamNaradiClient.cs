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

        public async Task<ZiskatSeznamNaradiResponse> Handle(ZiskatSeznamNaradiRequest request)
        {
            var json = new RestClientJson(_url + "SeznamNaradi", _client)
                .AddParameter("offset", request.Offset.ToString())
                .AddParameter("pocet", request.MaxPocet.ToString());
            var result = await json.Execute();
            return result.GetPayload<ZiskatSeznamNaradiResponse>();
        }

        public async Task<OvereniUnikatnostiResponse> Handle(OvereniUnikatnostiRequest request)
        {
            var json = new RestClientJson(_url + "OveritUnikatnost", _client)
                .AddParameter("vykres", request.Vykres)
                .AddParameter("rozmer", request.Rozmer);
            var result = await json.Execute();
            return result.GetPayload<OvereniUnikatnostiResponse>();
        }
    }
}
