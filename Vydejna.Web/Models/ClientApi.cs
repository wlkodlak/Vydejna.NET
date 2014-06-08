using ServiceLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Vydejna.Contracts;

namespace Vydejna.Web.Models
{
    public class ClientApi
    {
        private string _urlBase;

        public ClientApi(string urlBase)
        {
            _urlBase = urlBase;
        }

        public Task<ZiskatSeznamNaradiResponse> Query(ZiskatSeznamNaradiRequest request)
        {
            var client = new RestClientJson(_urlBase + "seznamnaradi/vsechno", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            return ExecuteQuery<ZiskatSeznamNaradiResponse>(client);
        }

        public Task<DetailNaradiResponse> Query(DetailNaradiRequest request)
        {
            var client = new RestClientJson(_urlBase + "seznamnaradi/detail", new HttpClient());
            client.AddParameter("naradiid", request.NaradiId.ToString());
            return ExecuteQuery<DetailNaradiResponse>(client);
        }

        public Task<PrehledNaradiResponse> Query(PrehledNaradiRequest request)
        {
            var client = new RestClientJson(_urlBase + "seznamnaradi/prehled", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            return ExecuteQuery<PrehledNaradiResponse>(client);
        }

        public Task<ZiskatNaradiNaVydejneResponse> Query(ZiskatNaradiNaVydejneRequest request)
        {
            var client = new RestClientJson(_urlBase + "seznamnaradi/navydejne", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            return ExecuteQuery<ZiskatNaradiNaVydejneResponse>(client);
        }

        public Task<OvereniUnikatnostiResponse> Query(OvereniUnikatnostiRequest request)
        {
            var client = new RestClientJson(_urlBase + "seznamnaradi/overitunikatnost", new HttpClient());
            client.AddParameter("vykres", request.Vykres);
            client.AddParameter("rozmer", request.Rozmer);
            return ExecuteQuery<OvereniUnikatnostiResponse>(client);
        }

        public Task<PrehledCislovanehoNaradiResponse> Query(PrehledCislovanehoNaradiRequest request)
        {
            var client = new RestClientJson(_urlBase + "cislovane/prehled", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            return ExecuteQuery<PrehledCislovanehoNaradiResponse>(client);
        }

        public Task<NajitObjednavkuResponse> Query(NajitObjednavkuRequest request)
        {
            var client = new RestClientJson(_urlBase + "objednavky/najit", new HttpClient());
            client.AddParameter("objednavka", request.Objednavka);
            return ExecuteQuery<NajitObjednavkuResponse>(client);
        }

        public Task<NajitDodaciListResponse> Query(NajitDodaciListRequest request)
        {
            var client = new RestClientJson(_urlBase + "objednavky/dodacilist", new HttpClient());
            client.AddParameter("dodacilist", request.DodaciList);
            return ExecuteQuery<NajitDodaciListResponse>(client);
        }

        public Task<ZiskatNaradiNaObjednavceResponse> Query(ZiskatNaradiNaObjednavceRequest request)
        {
            var client = new RestClientJson(_urlBase + "objednavky/naradi", new HttpClient());
            client.AddParameter("objednavka", request.Objednavka);
            client.AddParameter("dodavatel", request.KodDodavatele);
            return ExecuteQuery<ZiskatNaradiNaObjednavceResponse>(client);
        }

        public Task<PrehledObjednavekResponse> Query(PrehledObjednavekRequest request)
        {
            var client = new RestClientJson(_urlBase + "objednavky/prehled", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            client.AddParameter("razeni", request.Razeni.ToString());
            return ExecuteQuery<PrehledObjednavekResponse>(client);
        }

        public Task<ZiskatSeznamPracovistResponse> Query(ZiskatSeznamPracovistRequest request)
        {
            var client = new RestClientJson(_urlBase + "pracoviste/seznam", new HttpClient());
            client.AddParameter("stranka", request.Stranka.ToString(CultureInfo.InvariantCulture));
            return ExecuteQuery<ZiskatSeznamPracovistResponse>(client);
        }

        public Task<ZiskatNaradiNaPracovistiResponse> Query(ZiskatNaradiNaPracovistiRequest request)
        {
            var client = new RestClientJson(_urlBase + "pracoviste/naradi", new HttpClient());
            client.AddParameter("pracoviste", request.KodPracoviste);
            return ExecuteQuery<ZiskatNaradiNaPracovistiResponse>(client);
        }

        public Task<ZiskatSeznamDodavateluResponse> Query(ZiskatSeznamDodavateluRequest request)
        {
            var client = new RestClientJson(_urlBase + "dodavatele/seznam", new HttpClient());
            return ExecuteQuery<ZiskatSeznamDodavateluResponse>(client);
        }

        public Task<ZiskatSeznamVadResponse> Query(ZiskatSeznamVadRequest request)
        {
            var client = new RestClientJson(_urlBase + "vady/seznam", new HttpClient());
            return ExecuteQuery<ZiskatSeznamVadResponse>(client);
        }

        private Task<T> ExecuteQuery<T>(RestClient client)
        {
            return client.Execute().ContinueWith<T>(task =>
            {
                var result = task.Result;
                if (result.StatusCode == 200)
                    return result.GetPayload<T>();
                else
                    throw new ClientApiException(result.GetPayload<string>());
            });
        }
    }

    public class ClientApiException : Exception
    {
        public ClientApiException(string message)
            : base(message)
        {
        }
    }
}