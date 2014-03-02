using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NaradiNaVydejneProjection
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
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent>>
    {
        private IDocumentFolder _store;
        private NaradiNaVydejneSerializer _serializer;
        private NaradiNaVydejneDataCiselnik _ciselnikData;
        private NaradiNaVydejneDataNaVydejne _vydejnaData;
        private int _ciselnikVerze, _vydejnaVerze;
        private bool _ciselnikDirty, _vydejnaDirty;

        public NaradiNaVydejneProjection(IDocumentFolder store)
        {
            _store = store;
            _serializer = new NaradiNaVydejneSerializer();
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return storedVersion == GetVersion() ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _vydejnaData = _serializer.NacistDataKompletne(null);
            _ciselnikData = _serializer.NacistCiselnik(null);
            _ciselnikDirty = _vydejnaDirty = false;
            _ciselnikVerze = _vydejnaVerze = 0;
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
            private NaradiNaVydejneProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public ResumeWorker(NaradiNaVydejneProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._store.GetDocument("ciselnik", NactenaDataCiselniku, () => NactenaDataCiselniku(0, null), _onError);
            }

            private void NactenaDataCiselniku(int verze, string raw)
            {
                _parent._ciselnikData = _parent._serializer.NacistCiselnik(raw);
                _parent._ciselnikVerze = verze;
                _parent._ciselnikDirty = false;

                _parent._store.GetDocument("navydejne", NactenaDataVydejny, () => NactenaDataVydejny(0, null), _onError);
            }

            private void NactenaDataVydejny(int verze, string raw)
            {
                _parent._vydejnaData = _parent._serializer.NacistDataKompletne(raw);
                _parent._vydejnaDirty = false;
                _parent._vydejnaVerze = verze;

                _onComplete();
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private NaradiNaVydejneProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushWorker(NaradiNaVydejneProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                if (_parent._ciselnikDirty)
                {
                    _parent._store.SaveDocument("ciselnik",
                        _parent._serializer.UlozitCiselnik(_parent._ciselnikData),
                        DocumentStoreVersion.At(_parent._ciselnikVerze),
                        null, () => { _parent._ciselnikVerze++; UlozitData(); }, Concurrency, _onError);
                }
                else
                    UlozitData();
            }

            private void UlozitData()
            {
                if (_parent._vydejnaDirty)
                {
                    _parent._store.SaveDocument("navydejne",
                        _parent._serializer.UlozitData(_parent._vydejnaData),
                        DocumentStoreVersion.At(_parent._vydejnaVerze),
                        null, () => { _parent._vydejnaVerze++; _onComplete(); }, Concurrency, _onError);
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
            InformaceONaradi naradi;
            if (!_ciselnikData.Index.TryGetValue(message.Command.NaradiId, out naradi))
            {
                naradi = new InformaceONaradi();
                naradi.NaradiId = message.Command.NaradiId;
                _ciselnikData.Index[naradi.NaradiId] = naradi;
            }
            naradi.Vykres = message.Command.Vykres;
            naradi.Rozmer = message.Command.Rozmer;
            naradi.Druh = message.Command.Druh;
            _ciselnikDirty = true;
        }

        private void UpravitNaradi(Action onComplete, Action<Exception> onError, Guid naradiId, int cisloNaradi, StavNaradi stavNaradi, int pocetCelkem)
        {
            NaradiNaVydejne umistene;
            InformaceONaradi definice;

            if (!_ciselnikData.Index.TryGetValue(naradiId, out definice))
            {
                definice = new InformaceONaradi();
                definice.NaradiId = naradiId;
                definice.Vykres = definice.Rozmer = definice.Druh = "";
            }

            var klic = Tuple.Create(naradiId, stavNaradi);
            if (_vydejnaData.Index.TryGetValue(klic, out umistene))
            {
                umistene.Vykres = definice.Vykres;
                umistene.Rozmer = definice.Rozmer;
                umistene.Druh = definice.Druh;
                if (cisloNaradi == 0)
                {
                    umistene.PocetNecislovanych = pocetCelkem;
                }
                else
                {
                    if (pocetCelkem == 0)
                        umistene.SeznamCislovanych.Remove(cisloNaradi);
                    else if (!umistene.SeznamCislovanych.Contains(cisloNaradi))
                        umistene.SeznamCislovanych.Add(cisloNaradi);
                }
                umistene.PocetCelkem = umistene.PocetCelkem + umistene.SeznamCislovanych.Count;

                if (umistene.PocetCelkem == 0)
                {
                    _vydejnaData.Index.Remove(klic);
                    _vydejnaData.Seznam.Remove(umistene);
                }

                onComplete();
            }
            else if (pocetCelkem > 0)
            {
                umistene.Vykres = definice.Vykres;
                umistene.Rozmer = definice.Rozmer;
                umistene.Druh = definice.Druh;
                if (cisloNaradi == 0)
                    umistene.PocetNecislovanych = pocetCelkem;
                else
                    umistene.SeznamCislovanych.Add(cisloNaradi);
                umistene.PocetCelkem = pocetCelkem;
                _vydejnaData.Seznam.Add(umistene);
                onComplete();
            }
            
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.VPoradku, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.VPoradku, 0);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, message.Command.StavNaradi, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.NutnoOpravit, 0);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, message.Command.StavNaradi, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.Neopravitelne, 0);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.VPoradku, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.VPoradku, message.Command.PocetNaPredchozim);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.StavNaradi, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.NutnoOpravit, message.Command.PocetNaPredchozim);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.StavNaradi, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.Neopravitelne, message.Command.PocetNaPredchozim);
        }
    }

    public class NaradiNaVydejneDataNaVydejne
    {
        public List<NaradiNaVydejne> Seznam { get; set; }
        public Dictionary<Tuple<Guid, StavNaradi>, NaradiNaVydejne> Index;
    }
    public class NaradiNaVydejneDataCiselnik
    {
        public List<InformaceONaradi> Seznam { get; set; }
        public Dictionary<Guid, InformaceONaradi> Index;
    }

    public class NaradiNaVydejneSerializer
    {
        public NaradiNaVydejneDataNaVydejne NacistDataKompletne(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaVydejneDataNaVydejne>(raw);
            result = result ?? new NaradiNaVydejneDataNaVydejne();
            result.Seznam = result.Seznam ?? new List<NaradiNaVydejne>();
            result.Index = new Dictionary<Tuple<Guid, StavNaradi>, NaradiNaVydejne>(result.Seznam.Count);
            foreach (var naradi in result.Seznam)
            {
                result.Index[Tuple.Create(naradi.NaradiId, naradi.StavNaradi)] = naradi;
            }
            return result;
        }

        public string UlozitData(NaradiNaVydejneDataNaVydejne data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public NaradiNaVydejneDataNaVydejne NacistDataProReader(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaVydejneDataNaVydejne>(raw);
        }

        public NaradiNaVydejneDataCiselnik NacistCiselnik(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaVydejneDataCiselnik>(raw);
            result = result ?? new NaradiNaVydejneDataCiselnik();
            if (result.Seznam == null)
                result.Index = new Dictionary<Guid, InformaceONaradi>();
            else
            {
                result.Index = new Dictionary<Guid, InformaceONaradi>(result.Seznam.Count);
                foreach (var naradi in result.Seznam)
                    result.Index[naradi.NaradiId] = naradi;
            }
            return result;
        }

        public string UlozitCiselnik(NaradiNaVydejneDataCiselnik data)
        {
            data.Seznam = data.Index.Values.ToList();
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class NaradiNaVydejneReader
        : IAnswer<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<ZiskatNaradiNaVydejneResponse> _cache;
        private NaradiNaVydejneSerializer _serializer;

        public NaradiNaVydejneReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<ZiskatNaradiNaVydejneResponse>(executor, time);
            _serializer = new NaradiNaVydejneSerializer();
        }

        public void Handle(QueryExecution<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse> message)
        {
            _cache.Get("navydejne",
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load =>
                {
                    if (load.OldValueAvailable)
                    {
                        _store.GetNewerDocument(load.Key, load.OldVersion,
                            (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                            () => load.ValueIsStillValid(),
                            () => load.SetLoadedValue(0, VytvoritResponse(null)),
                            load.LoadingFailed);
                    }
                    else
                    {
                        _store.GetDocument(load.Key,
                            (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                            () => load.SetLoadedValue(0, VytvoritResponse(null)),
                            load.LoadingFailed);
                    }
                });
        }

        private ZiskatNaradiNaVydejneResponse VytvoritResponse(string raw)
        {
            var zaklad = _serializer.NacistDataProReader(raw);
            var response = new ZiskatNaradiNaVydejneResponse();
            response.Seznam = zaklad.Seznam ?? new List<NaradiNaVydejne>(); 
            return response;
        }
    }
}
