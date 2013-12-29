using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiReader : IReadSeznamNaradi
    {
        private IDocumentFolder _store;
        private SeznamNaradiSerializer _serializer;
        private object _lock;
        private IComparer<TypNaradiDto> _comparer;
        private bool _isLoading;
        private SeznamNaradiData _data;
        private List<Action<Exception>> _waitingOnError;
        private List<Action<SeznamNaradiData>> _waitingOnComplete;
        private IDisposable _sledovani;

        public SeznamNaradiReader(IDocumentFolder store, SeznamNaradiSerializer serializer)
        {
            _store = store;
            _serializer = serializer;
            _lock = new object();
            _comparer = new VykresRozmerComparer();
            _waitingOnComplete = new List<Action<SeznamNaradiData>>();
            _waitingOnError = new List<Action<Exception>>();
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
            if (_sledovani != null)
                _sledovani.Dispose();
        }

        private void GetCurrentData(Action<SeznamNaradiData> onComplete, Action<Exception> onError)
        {
            SledovatZmeny();
            SeznamNaradiData nactenaData = null;
            bool nacistData = false;
            lock (_lock)
            {
                if (_data != null)
                    nactenaData = _data;
                else
                {
                    _waitingOnComplete.Add(onComplete);
                    _waitingOnError.Add(onError);
                    if (!_isLoading)
                    {
                        _isLoading = true;
                        nacistData = true;
                    }
                }
            }
            if (nactenaData != null)
                onComplete(nactenaData);
            else if (nacistData)
                _store.GetDocument("data", NactenDokument, () => NactenDokument(0, null), ChybaNacitani);
        }

        private void SledovatZmeny()
        {
            lock (_lock)
            {
                if (_sledovani != null)
                    return;
                _sledovani = _store.WatchChanges("data", InvalidaceCache);
            }
        }

        private void InvalidaceCache()
        {
            lock (_lock)
                _data = null;
        }

        private void NactenDokument(int version, string contents)
        {
            List<Action<SeznamNaradiData>> handlers = null;
            List<Action<Exception>> errors = null;
            SeznamNaradiData data = null;
            try
            {
                lock (_lock)
                {
                    _isLoading = false;
                    handlers = _waitingOnComplete.ToList();
                    errors = _waitingOnError.ToList();
                    _waitingOnComplete.Clear();
                    _waitingOnError.Clear();
                    data = _data = _serializer.Deserialize(contents);
                }
                foreach (var handler in handlers)
                    handler(data);
            }
            catch (Exception ex)
            {
                foreach (var handler in errors)
                    handler(ex);
            }
        }

        private void ChybaNacitani(Exception exception)
        {
            List<Action<Exception>> errors = null;
            lock (_lock)
            {
                _isLoading = false;
                errors = _waitingOnError.ToList();
                _waitingOnComplete.Clear();
                _waitingOnError.Clear();
            }
            foreach (var handler in errors)
                handler(exception);
        }

        private class VykresRozmerComparer : IComparer<TypNaradiDto>
        {
            public int Compare(TypNaradiDto x, TypNaradiDto y)
            {
                int compare;
                compare = string.CompareOrdinal(x.Vykres, y.Vykres);
                if (compare != 0)
                    return compare;
                compare = string.CompareOrdinal(x.Rozmer, y.Rozmer);
                if (compare != 0)
                    return compare;
                return 0;
            }
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
                _parent.GetCurrentData(DataLoaded, OnError);
            }

            private void DataLoaded(SeznamNaradiData data)
            {
                var response = new ZiskatSeznamNaradiResponse();
                response.PocetCelkem = data.Seznam.Count;
                response.Offset = _request.Request.Offset;
                response.SeznamNaradi = data.Seznam.Skip(_request.Request.Offset).Take(_request.Request.MaxPocet).ToList();
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
                _parent.GetCurrentData(DataLoaded, OnError);
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

    public class SeznamNaradiSerializer
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
    }

    public class SeznamNaradiProjection
        : IPureProjection<SeznamNaradiData>
        , IPureProjectionHandler<SeznamNaradiData, DefinovanoNaradiEvent>
        , IPureProjectionHandler<SeznamNaradiData, AktivovanoNaradiEvent>
        , IPureProjectionHandler<SeznamNaradiData, DeaktivovanoNaradiEvent>
    {
        private IDocumentFolder _store;
        private SeznamNaradiSerializer _serializer;

        public SeznamNaradiProjection(IDocumentFolder store, SeznamNaradiSerializer serializer)
        {
            _store = store;
            _serializer = serializer;
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

        public IList<string> GetStreamPrefixes()
        {
            return new[] { "naradi" };
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
            return new SeznamNaradiData();
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
                state.Seznam.Add(naradi);
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
