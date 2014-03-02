using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using ServiceStack.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class PrehledCislovanehoNaradiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent>>
        , IHandle<CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent>>
    {
        private IDocumentFolder _store;
        private PrehledCislovanehoNaradiSerializer _serializer;
        private int _cislovaneVerze, _naradiVerze;
        private bool _cislovaneDirty, _naradiDirty;
        private PrehledCislovanehoNaradiDataCislovane _cislovaneData;
        private PrehledCislovanehoNaradiDataNaradi _naradiData;
        
        public PrehledCislovanehoNaradiProjection(IDocumentFolder store)
        {
            _store = store;
            _serializer = new PrehledCislovanehoNaradiSerializer();
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return GetVersion() == storedVersion ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _cislovaneData = _serializer.NacistCislovaneKompletne(null);
            _naradiData = _serializer.NacistCiselnik(null);
            _cislovaneDirty = _naradiDirty = false;
            _cislovaneVerze = _naradiVerze = 0;
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            new ResumeWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class ResumeWorker
        {
            private PrehledCislovanehoNaradiProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public ResumeWorker(PrehledCislovanehoNaradiProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._store.GetDocument("cislovane", NacistCislovane, () => NacistCislovane(0, null), _onError);
            }

            private void NacistCislovane(int verze, string raw)
            {
                _parent._cislovaneVerze = verze;
                _parent._cislovaneData = _parent._serializer.NacistCislovaneKompletne(raw);
                _parent._cislovaneDirty = false;

                _parent._store.GetDocument("naradi", NacistNaradi, () => NacistNaradi(0, null), _onError);
            }

            private void NacistNaradi(int verze, string raw)
            {
                _parent._naradiVerze = verze;
                _parent._naradiData = _parent._serializer.NacistCiselnik(raw);
                _parent._naradiDirty = false;

                _onComplete();
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private PrehledCislovanehoNaradiProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushWorker(PrehledCislovanehoNaradiProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                if (_parent._cislovaneDirty)
                {
                    _parent._store.SaveDocument("cislovane",
                        _parent._serializer.UlozitCislovane(_parent._cislovaneData),
                        DocumentStoreVersion.At(_parent._cislovaneVerze),
                        null,
                        () => { _parent._cislovaneVerze++; UlozitCiselnik(); },
                        Concurrency, _onError);
                }
                else
                    UlozitCiselnik();
            }

            private void UlozitCiselnik()
            {
                if (_parent._naradiDirty)
                {
                    _parent._store.SaveDocument("naradi",
                        _parent._serializer.UlozitCiselnik(_parent._naradiData),
                        DocumentStoreVersion.At(_parent._naradiVerze),
                        null, () => { _parent._naradiVerze++; _onComplete(); },
                        Concurrency, _onError);
                }
                else
                    _onComplete();
            }

            private void Concurrency()
            {
                _onError(new ProjectorMessages.ConcurrencyException());
            }
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            _naradiDirty = true;
            InformaceONaradi naradi;
            if (!_naradiData.Index.TryGetValue(message.Command.NaradiId, out naradi))
            {
                naradi = new InformaceONaradi();
                naradi.NaradiId = message.Command.NaradiId;
                _naradiData.Index[naradi.NaradiId] = naradi;
                _naradiData.Seznam.Add(naradi);
            }
            naradi.Vykres = message.Command.Vykres;
            naradi.Rozmer = message.Command.Rozmer;
            naradi.Druh = message.Command.Druh;

            foreach (var cislovane in _cislovaneData.Seznam)
            {
                if (cislovane.NaradiId == naradi.NaradiId)
                {
                    cislovane.Vykres = naradi.Vykres;
                    cislovane.Rozmer = naradi.Rozmer;
                    cislovane.Druh = naradi.Druh;
                    _cislovaneDirty = true;
                }
            }
        }

        private void PresunNaradi(Guid naradiId, int cisloNaradi, UmisteniNaradiDto umisteni, decimal cena, Action onCompleted)
        {
            CislovaneNaradiVPrehledu cislovane;
            if (!_cislovaneData.Index.TryGetValue(cisloNaradi, out cislovane))
            {
                cislovane = new CislovaneNaradiVPrehledu();
                cislovane.NaradiId = naradiId;
                InformaceONaradi definice;
                if (_naradiData.Index.TryGetValue(cislovane.NaradiId, out definice))
                {
                    cislovane.Vykres = definice.Vykres;
                    cislovane.Rozmer = definice.Rozmer;
                    cislovane.Druh = definice.Druh;
                }
                cislovane.CisloNaradi = cisloNaradi;
                _cislovaneData.Index[cisloNaradi] = cislovane;
                _cislovaneData.Seznam.Add(cislovane);
            }
            cislovane.Umisteni = umisteni;
            cislovane.Cena = cena;

            if (umisteni.ZakladniUmisteni == ZakladUmisteni.VeSrotu)
            {
                _cislovaneData.Seznam.Remove(cislovane);
                _cislovaneData.Index.Remove(cislovane.CisloNaradi);
            }

            onCompleted();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            PresunNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.NoveUmisteni, message.Command.CenaNova, message.OnCompleted);
        }
    }

    public class PrehledCislovanehoNaradiDataCislovane
    {
        public List<CislovaneNaradiVPrehledu> Seznam { get; set; }
        public Dictionary<int, CislovaneNaradiVPrehledu> Index;
    }
    public class PrehledCislovanehoNaradiDataNaradi
    {
        public List<InformaceONaradi> Seznam { get; set; }
        public Dictionary<Guid, InformaceONaradi> Index;
    }

    public class PrehledCislovanehoNaradiSerializer
    {
        public string UlozitCislovane(PrehledCislovanehoNaradiDataCislovane data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public string UlozitCiselnik(PrehledCislovanehoNaradiDataNaradi data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public PrehledCislovanehoNaradiDataCislovane NacistCislovaneKompletne(string raw)
        {
            var result = NacistCislovaneProReader(raw);
            result.Index = new Dictionary<int, CislovaneNaradiVPrehledu>();
            foreach (var naradi in result.Seznam)
                result.Index[naradi.CisloNaradi] = naradi;
            return result;
        }

        public PrehledCislovanehoNaradiDataCislovane NacistCislovaneProReader(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledCislovanehoNaradiDataCislovane>(raw);
            result = result ?? new PrehledCislovanehoNaradiDataCislovane();
            result.Seznam = result.Seznam ?? new List<CislovaneNaradiVPrehledu>();
            return result;
        }

        public PrehledCislovanehoNaradiDataNaradi NacistCiselnik(string raw)
        {
            var result = JsonSerializer.DeserializeFromString<PrehledCislovanehoNaradiDataNaradi>(raw);
            result = result ?? new PrehledCislovanehoNaradiDataNaradi();
            result.Seznam = result.Seznam ?? new List<InformaceONaradi>();
            result.Index = new Dictionary<Guid, InformaceONaradi>();
            foreach (var naradi in result.Seznam)
                result.Index[naradi.NaradiId] = naradi;
            return result;
        }
    }

    public class PrehledCislovanehoNaradiReader
        : IAnswer<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<PrehledCislovanehoNaradiResponse> _cache;
        private PrehledCislovanehoNaradiSerializer _serializer;

        public PrehledCislovanehoNaradiReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<PrehledCislovanehoNaradiResponse>(executor, time);
            _serializer = new PrehledCislovanehoNaradiSerializer();
        }

        public void Handle(QueryExecution<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse> message)
        {
            _cache.Get("cislovane", (verze, data) => message.OnCompleted(data), message.OnError, NacistCislovane);
        }

        private void NacistCislovane(IMemoryCacheLoad<PrehledCislovanehoNaradiResponse> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(load.Key, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    ex => load.LoadingFailed(ex));
            }
            else
            {
                _store.GetDocument(load.Key, 
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    ex => load.LoadingFailed(ex));
            }
        }

        private PrehledCislovanehoNaradiResponse VytvoritResponse(string raw)
        {
            var zaklad = _serializer.NacistCislovaneProReader(raw);
            return new PrehledCislovanehoNaradiResponse() { Seznam = zaklad.Seznam };
        }
    }
}
