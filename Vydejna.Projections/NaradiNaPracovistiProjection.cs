using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Projections.NaradiNaPracovistiReadModel
{
    public class NaradiNaPracovistiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
        , IHandle<CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
    {
        private NaradiNaPracovistiRepository _repository;
        private MemoryCache<NaradiNaPracovistiDataPracoviste> _cachePracovist;
        private MemoryCache<InformaceONaradi> _cacheNaradi;

        public NaradiNaPracovistiProjection(NaradiNaPracovistiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cachePracovist = new MemoryCache<NaradiNaPracovistiDataPracoviste>(executor, time);
            _cacheNaradi = new MemoryCache<InformaceONaradi>(executor, time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoPracovisteEvent>(this);
            mgr.Register<CislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
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
            _cacheNaradi.Clear();
            _cachePracovist.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushExecutor(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushExecutor
        {
            private NaradiNaPracovistiProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushExecutor(NaradiNaPracovistiProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._cacheNaradi.Flush(UlozitPracoviste, _onError, save => _parent._repository.UlozitNaradi(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void UlozitPracoviste()
            {
                _parent._cachePracovist.Flush(_onComplete, _onError, save => _parent._repository.UlozitPracoviste(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
        }

        private class NaradiIdComparer : IComparer<NaradiNaPracovisti>
        {
            public int Compare(NaradiNaPracovisti x, NaradiNaPracovisti y)
            {
                return x.NaradiId.CompareTo(y.NaradiId);
            }
        }

        private class UpravitNaradiNaPracovisti
        {
            private NaradiNaPracovistiProjection _parent;
            private string _kodPracoviste;
            private Action _onComplete;
            private Action<Exception> _onError;
            private Guid _naradiId;
            private int _cisloNaradi;
            private int _novyPocet;
            private DateTime? _datumVydeje;

            private NaradiNaPracovistiDataPracoviste _dataPracoviste;
            private InformaceONaradi _naradiInfo;
            private int _verzePracoviste;

            public UpravitNaradiNaPracovisti(NaradiNaPracovistiProjection parent, string kodPracoviste, Action onComplete, Action<Exception> onError, Guid naradiId, int cisloNaradi, int novyPocet, DateTime? datumVydeje)
            {
                _parent = parent;
                _kodPracoviste = kodPracoviste;
                _onComplete = onComplete;
                _onError = onError;
                _naradiId = naradiId;
                _cisloNaradi = cisloNaradi;
                _novyPocet = novyPocet;
                _datumVydeje = datumVydeje;
            }

            public void Execute()
            {
                _parent._cachePracovist.Get(_kodPracoviste, NactenoPracoviste, _onError,
                    load => _parent._repository.NacistPracoviste(_kodPracoviste, load.OldVersion,
                        (verze, data) => load.SetLoadedValue(verze, RozsiritData(_kodPracoviste, data)),
                        load.ValueIsStillValid, load.LoadingFailed));
            }

            private void NactenoPracoviste(int verzePracoviste, NaradiNaPracovistiDataPracoviste dataPracoviste)
            {
                _verzePracoviste = verzePracoviste;
                _dataPracoviste = dataPracoviste;

                _parent._cacheNaradi.Get(_naradiId.ToString(), NactenoNaradi, _onError,
                    load => _parent._repository.NacistNaradi(_naradiId, load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenoNaradi(int verzeNaradi, InformaceONaradi naradiInfo)
            {
                _naradiInfo = naradiInfo;

                ExecuteInternal();
                _parent._cachePracovist.Insert(_kodPracoviste, _verzePracoviste, _dataPracoviste, dirty: true);
                _onComplete();
            }

            private void ExecuteInternal()
            {
                NaradiNaPracovisti naradiNaPracovisti;
                if (!_dataPracoviste.IndexPodleIdNaradi.TryGetValue(_naradiId, out naradiNaPracovisti))
                {
                    _dataPracoviste.IndexPodleIdNaradi[_naradiId] = naradiNaPracovisti = new NaradiNaPracovisti();
                    naradiNaPracovisti.NaradiId = _naradiId;
                    naradiNaPracovisti.SeznamCislovanych = new List<int>();
                    var naradiComparer = new NaradiIdComparer();
                    var index = _dataPracoviste.Seznam.BinarySearch(naradiNaPracovisti, naradiComparer);
                    if (index < 0)
                        index = ~index;
                    _dataPracoviste.Seznam.Insert(index, naradiNaPracovisti);
                }
                if (_naradiInfo != null)
                {
                    naradiNaPracovisti.Vykres = _naradiInfo.Vykres;
                    naradiNaPracovisti.Rozmer = _naradiInfo.Rozmer;
                    naradiNaPracovisti.Druh = _naradiInfo.Druh;
                }

                if (_datumVydeje.HasValue)
                    naradiNaPracovisti.DatumPoslednihoVydeje = _datumVydeje.Value;
                if (_cisloNaradi == 0)
                {
                    naradiNaPracovisti.PocetNecislovanych = _novyPocet;
                }
                else
                {
                    if (_novyPocet == 1 && !naradiNaPracovisti.SeznamCislovanych.Contains(_cisloNaradi))
                        naradiNaPracovisti.SeznamCislovanych.Add(_cisloNaradi);
                    else if (_novyPocet == 0)
                        naradiNaPracovisti.SeznamCislovanych.Remove(_cisloNaradi);
                }
                naradiNaPracovisti.PocetCelkem = naradiNaPracovisti.PocetNecislovanych + naradiNaPracovisti.SeznamCislovanych.Count;
                if (naradiNaPracovisti.PocetCelkem == 0)
                {
                    _dataPracoviste.Seznam.Remove(naradiNaPracovisti);
                    _dataPracoviste.IndexPodleIdNaradi.Remove(naradiNaPracovisti.NaradiId);
                }
                _dataPracoviste.PocetCelkem = _dataPracoviste.IndexPodleIdNaradi.Values.Sum(n => n.PocetCelkem);
            }
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            _cachePracovist.Get(message.Command.Kod,
                (verze, data) => ZpracovatDefiniciPracoviste(message, verze, data),
                message.OnError,
                load => _repository.NacistPracoviste(message.Command.Kod, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, RozsiritData(message.Command.Kod, data)),
                    load.ValueIsStillValid, load.LoadingFailed));
        }

        private void ZpracovatDefiniciPracoviste(CommandExecution<DefinovanoPracovisteEvent> message, int verze, NaradiNaPracovistiDataPracoviste data)
        {
            data.Pracoviste = new InformaceOPracovisti
            {
                Kod = message.Command.Kod,
                Nazev = message.Command.Nazev,
                Stredisko = message.Command.Stredisko,
                Aktivni = !message.Command.Deaktivovano
            };

            _cachePracovist.Insert(message.Command.Kod, verze, data, dirty: true);
            message.OnCompleted();
        }

        private static NaradiNaPracovistiDataPracoviste RozsiritData(string kodPracoviste, NaradiNaPracovistiDataPracoviste data)
        {
            if (data == null)
            {
                data = new NaradiNaPracovistiDataPracoviste();
                data.Seznam = new List<NaradiNaPracovisti>();
                data.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaPracovisti>();
            }
            else if (data.IndexPodleIdNaradi == null)
            {
                data.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaPracovisti>();
                foreach (var naradi in data.Seznam)
                    data.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
            }
            if (data.Pracoviste == null)
            {
                data.Pracoviste = new InformaceOPracovisti();
                data.Pracoviste.Kod = kodPracoviste;
                data.Pracoviste.Nazev = "";
                data.Pracoviste.Stredisko = "";
            }
            return data;
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            new UpravitNaradiNaPracovisti(this, message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, 1, message.Command.Datum).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            new UpravitNaradiNaPracovisti(this, message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, 0, null).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            new UpravitNaradiNaPracovisti(this, message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.PocetNaNovem, message.Command.Datum).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            new UpravitNaradiNaPracovisti(this, message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.PocetNaPredchozim, null).Execute();
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            _cacheNaradi.Get(
                message.Command.NaradiId.ToString(),
                (verze, naradiInfo) => naradiInfo = ZpracovatDefiniciNaradi(message, verze, naradiInfo),
                message.OnError,
                load => _repository.NacistNaradi(message.Command.NaradiId, load.SetLoadedValue, load.LoadingFailed));
        }

        private InformaceONaradi ZpracovatDefiniciNaradi(CommandExecution<DefinovanoNaradiEvent> message, int verze, InformaceONaradi naradiInfo)
        {
            naradiInfo = new InformaceONaradi();
            naradiInfo.NaradiId = message.Command.NaradiId;
            naradiInfo.Vykres = message.Command.Vykres;
            naradiInfo.Rozmer = message.Command.Rozmer;
            naradiInfo.Druh = message.Command.Druh;
            _cacheNaradi.Insert(naradiInfo.NaradiId.ToString(), verze, naradiInfo, dirty: true);
            message.OnCompleted();
            return naradiInfo;
        }
    }

    public class NaradiNaPracovistiDataPracoviste
    {
        public InformaceOPracovisti Pracoviste { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaPracovisti> Seznam { get; set; }
        public Dictionary<Guid, NaradiNaPracovisti> IndexPodleIdNaradi;
    }

    public class NaradiNaPracovistiRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaPracovistiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void NacistPracoviste(string kodPracoviste, int znamaVerze, Action<int, NaradiNaPracovistiDataPracoviste> onLoaded, Action onValid, Action<Exception> onError)
        {
            _folder.GetNewerDocument(
                DokumentPracoviste(kodPracoviste), znamaVerze,
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaPracovistiDataPracoviste>(raw)),
                () => onValid(),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        private static string DokumentPracoviste(string kodPracoviste)
        {
            return "pracoviste-" + kodPracoviste;
        }

        public void UlozitPracoviste(int verze, NaradiNaPracovistiDataPracoviste data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "pracoviste-" + data.Pracoviste.Kod,
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze), null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistNaradi(Guid naradiId, Action<int, InformaceONaradi> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                DokumentNaradi(naradiId),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceONaradi>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitNaradi(int verze, InformaceONaradi data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                DokumentNaradi(data.NaradiId),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze), null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        private static string DokumentNaradi(Guid naradiId)
        {
            return "naradi-" + naradiId.ToString("N");
        }

        public void Reset(Action onCompleted, Action<Exception> onError)
        {
            _folder.DeleteAll(onCompleted, onError);
        }
    }

    public class NaradiNaPracovistiReader
        : IAnswer<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>
    {
        private NaradiNaPracovistiRepository _repository;
        private MemoryCache<ZiskatNaradiNaPracovistiResponse> _cache;

        public NaradiNaPracovistiReader(NaradiNaPracovistiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatNaradiNaPracovistiResponse>(executor, time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<QueryExecution<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>>(this);
        }

        public void Handle(QueryExecution<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse> message)
        {
            _cache.Get(message.Request.KodPracoviste, (verze, data) => message.OnCompleted(data), message.OnError,
                load => _repository.NacistPracoviste(message.Request.KodPracoviste, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponse(message.Request.KodPracoviste, data)),
                    load.ValueIsStillValid, load.LoadingFailed));
        }

        private ZiskatNaradiNaPracovistiResponse VytvoritResponse(string kodPracoviste, NaradiNaPracovistiDataPracoviste zaklad)
        {
            if (zaklad == null)
            {
                return new ZiskatNaradiNaPracovistiResponse
                {
                    Pracoviste = new InformaceOPracovisti
                    {
                        Kod = kodPracoviste,
                        Aktivni = false,
                        Nazev = "",
                        Stredisko = ""
                    },
                    PracovisteExistuje = false,
                    PocetCelkem = 0,
                    Seznam = new List<NaradiNaPracovisti>()
                };
            }
            else
            {
                return new ZiskatNaradiNaPracovistiResponse
                {
                    Pracoviste = zaklad.Pracoviste,
                    PracovisteExistuje = true,
                    Seznam = zaklad.Seznam,
                    PocetCelkem = zaklad.PocetCelkem
                };
            }
        }
    }
}
