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

        public async Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            var json = new RestClientJson(_url + "SeznamNaradi", _client)
                .AddParameter("offset", offset.ToString())
                .AddParameter("pocet", maxPocet.ToString());
            var result = await json.Execute();
            return result.GetPayload<SeznamNaradiDto>();
        }

        public async Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            var json = new RestClientJson(_url + "OveritUnikatnost", _client)
                .AddParameter("vykres", vykres)
                .AddParameter("rozmer", rozmer);
            var result = await json.Execute();
            return result.GetPayload<OvereniUnikatnostiDto>();
        }
    }
}
