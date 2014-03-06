using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiReader
        : IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>
        , IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>
    {
        private PureProjectionReader<SeznamNaradiData> _reader;

        public SeznamNaradiReader(IDocumentFolder store, SeznamNaradiSerializer serializer, IQueueExecution executor, ITime time, INotifyChange notifier)
        {
            _reader = new PureProjectionReader<SeznamNaradiData>(store, serializer, notifier, executor, time);
        }

        public SeznamNaradiReader SetupFreshness(int validityMs, int expirationMs)
        {
            _reader.SetupExpiration(validityMs, expirationMs);
            return this;
        }

        public void Handle(QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> request)
        {
            new ZiskatSeznamNaradiWorker(this, request).Execute();
        }

        public void Handle(QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> request)
        {
            new OvereniUnikatnostiWorker(this, request).Execute();
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        private class ZiskatSeznamNaradiWorker
        {
            private SeznamNaradiReader _parent;
            private QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> _request;

            public ZiskatSeznamNaradiWorker(SeznamNaradiReader parent, QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> request)
            {
                this._parent = parent;
                this._request = request;
            }

            public void Execute()
            {
                _parent._reader.Get("data", DataLoaded, OnError);
            }

            private void DataLoaded(SeznamNaradiData data)
            {
                var response = new ZiskatSeznamNaradiResponse();
                response.PocetCelkem = data.Seznam.Count;
                response.PocetStranek = (
                    data.Seznam.Count + 
                    ZiskatSeznamNaradiRequest.VelikostStranky - 1
                    ) / ZiskatSeznamNaradiRequest.VelikostStranky;
                response.Stranka = _request.Request.Stranka;
                var offset = (response.Stranka - 1) * ZiskatSeznamNaradiRequest.VelikostStranky;
                response.SeznamNaradi = data.Seznam.Skip(offset).Take(ZiskatSeznamNaradiRequest.VelikostStranky).ToList();
                _request.OnCompleted(response);
            }

            private void OnError(Exception exception)
            {
                _request.OnError(exception);
            }
        }

        private class OvereniUnikatnostiWorker
        {
            private SeznamNaradiReader _parent;
            private QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> _request;

            public OvereniUnikatnostiWorker(SeznamNaradiReader parent, QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> request)
            {
                this._parent = parent;
                this._request = request;
            }

            public void Execute()
            {
                _parent._reader.Get("data", DataLoaded, OnError);
            }

            private void DataLoaded(SeznamNaradiData data)
            {
                var response = new OvereniUnikatnostiResponse();
                response.Vykres = _request.Request.Vykres;
                response.Rozmer = _request.Request.Rozmer;
                response.Existuje = data.ExistujiciVykresy.Contains(Tuple.Create(_request.Request.Vykres, _request.Request.Rozmer));
                _request.OnCompleted(response);
            }

            private void OnError(Exception exception)
            {
                _request.OnError(exception);
            }
        }
    }

    public class SeznamNaradiData
    {
        public List<TypNaradiDto> Seznam { get; set; }
        public string Token { get; set; }
        public HashSet<Tuple<string, string>> ExistujiciVykresy;
        public Dictionary<Guid, TypNaradiDto> PodleId;
        public EventStoreToken LastToken;
        public SeznamNaradiData()
        {
            Seznam = new List<TypNaradiDto>();
            Token = "";
        }
    }

    public class SeznamNaradiSerializer : IPureProjectionSerializer<SeznamNaradiData>
    {
        public string Serialize(SeznamNaradiData data)
        {
            data.Token = data.LastToken.ToString();
            return JsonSerializer.SerializeToString(data);
        }
        public SeznamNaradiData Deserialize(string serialized)
        {
            var data = string.IsNullOrEmpty(serialized) ? new SeznamNaradiData() : JsonSerializer.DeserializeFromString<SeznamNaradiData>(serialized);
            data.ExistujiciVykresy = new HashSet<Tuple<string, string>>(data.Seznam.Select(n => Tuple.Create(n.Vykres, n.Rozmer)));
            data.PodleId = data.Seznam.ToDictionary(n => n.Id);
            data.LastToken = new EventStoreToken(data.Token);
            return data;
        }

        public SeznamNaradiData InitialState()
        {
            return Deserialize(null);
        }
    }

    public class SeznamNaradiProjection
        : IPureProjection<SeznamNaradiData>
        , IPureProjectionHandler<SeznamNaradiData, DefinovanoNaradiEvent>
        , IPureProjectionHandler<SeznamNaradiData, AktivovanoNaradiEvent>
        , IPureProjectionHandler<SeznamNaradiData, DeaktivovanoNaradiEvent>
    {
        private SeznamNaradiSerializer _serializer;
        private IComparer<TypNaradiDto> _dtoComparer;

        public SeznamNaradiProjection(SeznamNaradiSerializer serializer)
        {
            _serializer = serializer;
            _dtoComparer = new TypNaradiDtoPodleVykresu();
        }

        private class TypNaradiDtoPodleVykresu : IComparer<TypNaradiDto>
        {
            public int Compare(TypNaradiDto x, TypNaradiDto y)
            {
                int compare;
                if (ReferenceEquals(x, null))
                    return ReferenceEquals(y, null) ? 0 : -1;
                else if (ReferenceEquals(y, null))
                    return 1;
                compare = string.CompareOrdinal(x.Vykres, y.Vykres);
                if (compare != 0)
                    return compare;
                return string.CompareOrdinal(x.Rozmer, y.Rozmer);
            }
        }

        public void Subscribe(IPureProjectionDispatcher<SeznamNaradiData> dispatcher)
        {
            dispatcher.Register<DefinovanoNaradiEvent>(this);
            dispatcher.Register<AktivovanoNaradiEvent>(this);
            dispatcher.Register<DeaktivovanoNaradiEvent>(this);
        }

        public string GetVersion()
        {
            return "1.0";
        }

        public bool NeedsRebuild(string storedVersion)
        {
            return storedVersion != "1.0";
        }

        public string Serialize(SeznamNaradiData state)
        {
            return _serializer.Serialize(state);
        }

        public SeznamNaradiData Deserialize(string serializedState)
        {
            return _serializer.Deserialize(serializedState);
        }

        public SeznamNaradiData InitialState()
        {
            return _serializer.Deserialize(null);
        }

        public SeznamNaradiData SetTokenInState(SeznamNaradiData state, EventStoreToken token)
        {
            state.LastToken = token;
            return state;
        }

        public EventStoreToken GetTokenFromState(SeznamNaradiData state)
        {
            return state.LastToken;
        }

        public string Partition(DefinovanoNaradiEvent evnt)
        {
            return "data";
        }

        public SeznamNaradiData ApplyEvent(SeznamNaradiData state, DefinovanoNaradiEvent evnt, EventStoreToken token)
        {
            TypNaradiDto naradi;
            if (state.PodleId.TryGetValue(evnt.NaradiId, out naradi))
                naradi.Aktivni = true;
            else
            {
                naradi = new TypNaradiDto(evnt.NaradiId, evnt.Vykres, evnt.Rozmer, evnt.Druh, true);
                state.PodleId[naradi.Id] = naradi;
                state.ExistujiciVykresy.Add(Tuple.Create(evnt.Vykres, evnt.Rozmer));
                var index = state.Seznam.BinarySearch(naradi, _dtoComparer);
                if (index < 0)
                    index = ~index;
                state.Seznam.Insert(index, naradi);
            }
            return state;
        }

        public string Partition(AktivovanoNaradiEvent evnt)
        {
            return "data";
        }

        public SeznamNaradiData ApplyEvent(SeznamNaradiData state, AktivovanoNaradiEvent evnt, EventStoreToken token)
        {
            TypNaradiDto naradi;
            if (state.PodleId.TryGetValue(evnt.NaradiId, out naradi))
                naradi.Aktivni = true;
            return state;
        }

        public string Partition(DeaktivovanoNaradiEvent evnt)
        {
            return "data";
        }

        public SeznamNaradiData ApplyEvent(SeznamNaradiData state, DeaktivovanoNaradiEvent evnt, EventStoreToken token)
        {
            TypNaradiDto naradi;
            if (state.PodleId.TryGetValue(evnt.NaradiId, out naradi))
                naradi.Aktivni = false;
            return state;
        }
    }
}
