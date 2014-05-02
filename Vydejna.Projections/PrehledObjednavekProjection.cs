using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Projections.PrehledObjednavekReadModel
{
    public class PrehledObjednavekProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
    {
        private PrehledObjednavekRepository _repository;
        private MemoryCache<int> _cacheVerze;
        private MemoryCache<PrehledObjednavekDataObjednavky> _cacheObjednavek;
        private MemoryCache<PrehledObjednavekDataSeznamDodavatelu> _cacheDodavatelu;

        public PrehledObjednavekProjection(PrehledObjednavekRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheVerze = new MemoryCache<int>(executor, time);
            _cacheObjednavek = new MemoryCache<PrehledObjednavekDataObjednavky>(executor, time);
            _cacheDodavatelu = new MemoryCache<PrehledObjednavekDataSeznamDodavatelu>(executor, time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<DefinovanDodavatelEvent>(this);
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZOpravyEvent>(this);
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
            _cacheDodavatelu.Clear();
            _cacheObjednavek.Clear();
            _cacheVerze.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private PrehledObjednavekProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushWorker(PrehledObjednavekProjection parent, Action onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._onComplete = onComplete;
                this._onError = onError;
            }

            public void Execute()
            {
                _parent._cacheDodavatelu.Flush(FlushObjednavek, _onError,
                    save => _parent._repository.UlozitDodavatele(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void FlushObjednavek()
            {
                _parent._cacheObjednavek.Flush(FlushVerze, _onError,
                    save => _parent._repository.UlozitObjednavku(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void FlushVerze()
            {
                _parent._cacheVerze.Flush(_onComplete, _onError,
                    save => _parent._repository.UlozitVerziSeznamu(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            _cacheDodavatelu.Get(
                "dodavatele",
                (verze, dodavatele) => ZpracovatDefiniciDodavatele(verze, dodavatele, message),
                message.OnError, NacistDodavatele);
        }

        private void NacistDodavatele(IMemoryCacheLoad<PrehledObjednavekDataSeznamDodavatelu> load)
        {
            _repository.NacistDodavatele(
                (verze, dodavatele) => load.SetLoadedValue(verze, RozsiritData(dodavatele)),
                load.LoadingFailed
                );
        }

        private PrehledObjednavekDataSeznamDodavatelu RozsiritData(PrehledObjednavekDataSeznamDodavatelu dodavatele)
        {
            if (dodavatele == null)
            {
                dodavatele = new PrehledObjednavekDataSeznamDodavatelu();
                dodavatele.SeznamDodavatelu = new List<PrehledObjednavekDataDodavatele>();
                dodavatele.IndexDodavatelu = new Dictionary<string, PrehledObjednavekDataDodavatele>();
            }
            else if (dodavatele.IndexDodavatelu == null)
            {
                dodavatele.IndexDodavatelu = new Dictionary<string, PrehledObjednavekDataDodavatele>();
                foreach (var dodavatel in dodavatele.SeznamDodavatelu)
                    dodavatele.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
            }
            return dodavatele;
        }

        private void ZpracovatDefiniciDodavatele(int verze, PrehledObjednavekDataSeznamDodavatelu dodavatele, CommandExecution<DefinovanDodavatelEvent> message)
        {
            PrehledObjednavekDataDodavatele dodavatel;
            if (!dodavatele.IndexDodavatelu.TryGetValue(message.Command.Kod, out dodavatel))
            {
                dodavatel = new PrehledObjednavekDataDodavatele();
                dodavatel.KodDodavatele = message.Command.Kod;
                dodavatele.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
                dodavatele.SeznamDodavatelu.Add(dodavatel);
            }
            dodavatel.NazevDodavatele = message.Command.Nazev;
            _cacheDodavatelu.Insert("dodavatele", verze, dodavatele, dirty: true);

            // aktualizovat objednavky

            message.OnCompleted();
        }

        private class UpravitObjednavku
        {
            private PrehledObjednavekProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;
            private string _kodDodavatele;
            private string _cisloObjednavky;
            private Guid _eventId;
            private DateTime? _datum;
            private DateTime? _termin;
            private Action<ObjednavkaVPrehledu> _zmena;

            private int _verzeDokumentuVerze;
            private int _verzeSeznamu;
            private PrehledObjednavekDataDodavatele _dodavatel;
            private int _verzeObjednavky;
            private PrehledObjednavekDataObjednavky _objednavka;
            private string _dokumentObjednavky;

            public UpravitObjednavku(PrehledObjednavekProjection parent, Action onComplete, Action<Exception> onError,
                string kodDodavatele, string cisloObjednavky, Guid eventId, DateTime? datum, DateTime? termin, Action<ObjednavkaVPrehledu> zmena)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
                _kodDodavatele = kodDodavatele;
                _cisloObjednavky = cisloObjednavky;
                _eventId = eventId;
                _datum = datum;
                _termin = termin;
                _zmena = zmena;
                _dokumentObjednavky = PrehledObjednavekProjection.DokumentObjednavky(_kodDodavatele, _cisloObjednavky);
            }

            public void Execute()
            {
                _parent._cacheVerze.Get("verze", NactenaVerzeSeznamu, _onError,
                    load => _parent._repository.NacistVerziSeznamu(load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenaVerzeSeznamu(int verzeDokumentu, int verzeSeznamu)
            {
                _verzeDokumentuVerze = verzeDokumentu;
                _verzeSeznamu = verzeSeznamu;

                _parent._cacheDodavatelu.Get("dodavatele", NactenDodavatel, _onError, _parent.NacistDodavatele);
            }

            private void NactenDodavatel(int verzeDodavatelu, PrehledObjednavekDataSeznamDodavatelu seznamDodavatelu)
            {
                seznamDodavatelu.IndexDodavatelu.TryGetValue(_kodDodavatele, out _dodavatel);

                _parent._cacheObjednavek.Get(_dokumentObjednavky, NactenaObjednavka, _onError, NacistObjednavku);
            }

            private void NactenaObjednavka(int verzeObjednavky, PrehledObjednavekDataObjednavky objednavka)
            {
                _verzeObjednavky = verzeObjednavky;
                _objednavka = objednavka;

                ExecuteInternal();

                _parent._cacheObjednavek.Insert(_dokumentObjednavky, _verzeObjednavky, _objednavka, dirty: true);
                _parent._cacheVerze.Insert("verze", _verzeDokumentuVerze, _verzeSeznamu + 1, dirty: true);
                _onComplete();
            }

            private void NacistObjednavku(IMemoryCacheLoad<PrehledObjednavekDataObjednavky> load)
            {
                _parent._repository.NacistObjednavku(_kodDodavatele, _cisloObjednavky, load.SetLoadedValue, load.LoadingFailed);
            }

            private void ExecuteInternal()
            {
                if (_objednavka == null)
                {
                    _objednavka = new PrehledObjednavekDataObjednavky();
                    _objednavka.KodDodavatele = _kodDodavatele;
                    _objednavka.Objednavka = _cisloObjednavky;
                }

                if (_dodavatel != null)
                    _objednavka.NazevDodavatele = _dodavatel.NazevDodavatele;

                if (_datum.HasValue)
                    _objednavka.DatumObjednani = _datum.Value;
                if (_termin.HasValue)
                    _objednavka.TerminDodani = _termin.Value;

                _zmena(_objednavka);
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            new UpravitObjednavku(this, message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka,
                message.Command.EventId, message.Command.Datum, message.Command.TerminDodani,
                obj => { obj.PocetObjednanych += 1; }).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new UpravitObjednavku(this, message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka,
                message.Command.EventId, null, null,
                obj =>
                {
                    if (message.Command.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += 1;
                    else
                        obj.PocetOpravenych += 1;
                }).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            new UpravitObjednavku(this, message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka,
                message.Command.EventId, message.Command.Datum, message.Command.TerminDodani,
                obj => { obj.PocetObjednanych += message.Command.Pocet; }).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new UpravitObjednavku(this, message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka,
                message.Command.EventId, null, null,
                obj =>
                {
                    if (message.Command.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += message.Command.Pocet;
                    else
                        obj.PocetOpravenych += message.Command.Pocet;
                }).Execute();
        }

        public static string DokumentObjednavky(string kodDodavatele, string cisloObjednavky)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", kodDodavatele, "-", cisloObjednavky);
        }
    }

    public class PrehledObjednavekRepository
    {
        private IDocumentFolder _folder;

        public PrehledObjednavekRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void Reset(Action onCompleted, Action<Exception> onError)
        {
            _folder.DeleteAll(onCompleted, onError);
        }

        public void NacistObjednavku(string kodDodavatele, string cisloObjednavky, Action<int, PrehledObjednavekDataObjednavky> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                PrehledObjednavekProjection.DokumentObjednavky(kodDodavatele, cisloObjednavky),
                (verze, raw) => onLoaded(verze, DeserializovatObjednavku(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitObjednavku(int verze, PrehledObjednavekDataObjednavky data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                PrehledObjednavekProjection.DokumentObjednavky(data.KodDodavatele, data.Objednavka),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                IndexyObjednavky(data),
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        private DocumentIndexing[] IndexyObjednavky(PrehledObjednavekDataObjednavky data)
        {
            return new[]
            {
                new DocumentIndexing("cisloObjednavky", data.Objednavka),
                new DocumentIndexing("datumObjednavky", data.DatumObjednani.ToString("s"))
            };
        }

        private PrehledObjednavekDataObjednavky DeserializovatObjednavku(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            else
                return JsonSerializer.DeserializeFromString<PrehledObjednavekDataObjednavky>(raw);
        }

        public void NacistSeznamObjednavekPodleCisla(int offset, int pocet, Action<int, List<PrehledObjednavekDataObjednavky>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("cisloObjednavky", null, null, offset, pocet, true,
                list => onLoaded(list.TotalFound, list.Select(o => DeserializovatObjednavku(o.Contents)).ToList()), onError);
        }

        public void NacistSeznamObjednavekPodleData(int offset, int pocet, Action<int, List<PrehledObjednavekDataObjednavky>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("datumObjednavky", null, null, offset, pocet, false,
                list => onLoaded(list.TotalFound, list.Select(o => DeserializovatObjednavku(o.Contents)).ToList()), onError);
        }

        public void NacistDodavatele(Action<int, PrehledObjednavekDataSeznamDodavatelu> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("dodavatele",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledObjednavekDataSeznamDodavatelu>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitDodavatele(int verze, PrehledObjednavekDataSeznamDodavatelu data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "dodavatele",
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistVerziSeznamu(Action<int, int> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                "verzeSeznamu",
                (verze, raw) => onLoaded(verze, DeserializovatVerziSeznamu(raw)),
                () => onLoaded(0, 0),
                ex => onError(ex));
        }

        public void UlozitVerziSeznamu(int verzeDokumentu, int verzeSeznamu, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "verzeSeznamu",
                verzeSeznamu.ToString(),
                DocumentStoreVersion.At(verzeDokumentu),
                null,
                () => onSaved(verzeDokumentu + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        private int DeserializovatVerziSeznamu(string raw)
        {
            int verzeSeznamu = 0;
            if (!string.IsNullOrEmpty(raw))
                int.TryParse(raw, out verzeSeznamu);
            return verzeSeznamu;
        }
    }

    public class PrehledObjednavekDataObjednavky : ObjednavkaVPrehledu
    {
    }

    public class PrehledObjednavekDataSeznamDodavatelu
    {
        public List<PrehledObjednavekDataDodavatele> SeznamDodavatelu { get; set; }
        public Dictionary<string, PrehledObjednavekDataDodavatele> IndexDodavatelu;
    }
    public class PrehledObjednavekDataDodavatele
    {
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
    }

    public class PrehledObjednavekReader
        : IAnswer<PrehledObjednavekRequest, PrehledObjednavekResponse>
    {
        private PrehledObjednavekRepository _repository;
        private MemoryCache<int> _cacheVerze;
        private MemoryCache<PrehledObjednavekResponse> _cachePodleCisla;
        private MemoryCache<PrehledObjednavekResponse> _cachePodleData;

        public PrehledObjednavekReader(PrehledObjednavekRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheVerze = new MemoryCache<int>(executor, time);
            _cachePodleCisla = new MemoryCache<PrehledObjednavekResponse>(executor, time);
            _cachePodleData = new MemoryCache<PrehledObjednavekResponse>(executor, time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse>>(this);
        }

        public void Handle(QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse> message)
        {
            new Worker(this, message).Execute();
        }

        private class Worker
        {
            private PrehledObjednavekReader _parent;
            private QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse> _message;
            private int _verzeSeznamu;
            private int _offset;
            private int _pocet;
            private int _stranka;
            private PrehledObjednavekRazeni _razeni;

            public Worker(PrehledObjednavekReader parent, QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse> message)
            {
                this._parent = parent;
                this._message = message;
            }

            public void Execute()
            {
                _razeni = _message.Request.Razeni;
                _stranka = _message.Request.Stranka;
                _offset = _message.Request.Stranka * 100 - 100;
                _pocet = 100;

                _parent._cacheVerze.Get("verze", NactenaVerze, _message.OnError,
                    load => _parent._repository.NacistVerziSeznamu(load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenaVerze(int verzeDokumentu, int verzeSeznamu)
            {
                _verzeSeznamu = verzeSeznamu;
                if (_razeni == PrehledObjednavekRazeni.PodleCislaObjednavky)
                {
                    _parent._cachePodleCisla.Get(_stranka.ToString(), (verze, response) => _message.OnCompleted(response), _message.OnError, NacistPodleCisla);
                }
                else
                {
                    _parent._cachePodleData.Get(_stranka.ToString(), (verze, response) => _message.OnCompleted(response), _message.OnError, NacistPodleData);
                }
            }

            private void NacistPodleCisla(IMemoryCacheLoad<PrehledObjednavekResponse> load)
            {
                if (_verzeSeznamu == load.OldVersion)
                    load.ValueIsStillValid();
                else
                {
                    _parent._repository.NacistSeznamObjednavekPodleCisla(_offset, _pocet,
                        (celkem, seznam) => load.SetLoadedValue(_verzeSeznamu, VytvoritResponse(celkem, seznam)),
                        ex => load.LoadingFailed(ex));
                }
            }

            private void NacistPodleData(IMemoryCacheLoad<PrehledObjednavekResponse> load)
            {
                if (_verzeSeznamu == load.OldVersion)
                    load.ValueIsStillValid();
                else
                {
                    _parent._repository.NacistSeznamObjednavekPodleData(_offset, _pocet,
                        (celkem, seznam) => load.SetLoadedValue(_verzeSeznamu, VytvoritResponse(celkem, seznam)),
                        ex => load.LoadingFailed(ex));
                }
            }

            private PrehledObjednavekResponse VytvoritResponse(int celkem, List<PrehledObjednavekDataObjednavky> seznam)
            {
                return new PrehledObjednavekResponse
                {
                    Stranka = _stranka,
                    Razeni = _razeni,
                    PocetCelkem = celkem,
                    PocetStranek = (celkem + 99) / 100,
                    Seznam = seznam.Select(KonverzeNaOdpoved).ToList()
                };
            }

            private ObjednavkaVPrehledu KonverzeNaOdpoved(PrehledObjednavekDataObjednavky orig)
            {
                return new ObjednavkaVPrehledu
                {
                    DatumObjednani = orig.DatumObjednani,
                    KodDodavatele = orig.KodDodavatele,
                    NazevDodavatele = orig.NazevDodavatele,
                    Objednavka = orig.Objednavka,
                    TerminDodani = orig.TerminDodani,
                    PocetNeopravitelnych = orig.PocetNeopravitelnych,
                    PocetObjednanych = orig.PocetObjednanych,
                    PocetOpravenych = orig.PocetOpravenych
                };
            }
        }
    }
}
