using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Projections.SeznamNaradiReadModel
{
    public class SeznamNaradiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<AktivovanoNaradiEvent>>
        , IHandle<CommandExecution<DeaktivovanoNaradiEvent>>
    {
        private SeznamNaradiRepository _repository;
        private MemoryCache<TypNaradiDto> _cacheNaradi;
        private MemoryCache<int> _cacheTag;

        public SeznamNaradiProjection(SeznamNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<TypNaradiDto>(executor, time);
            _cacheTag = new MemoryCache<int>(executor, time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
            mgr.Register<AktivovanoNaradiEvent>(this);
            mgr.Register<DeaktivovanoNaradiEvent>(this);
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
            _cacheTag.Clear();
            _cacheNaradi.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            _cacheNaradi.Flush(
                () => _cacheTag.Flush(message.OnCompleted, message.OnError, save => _repository.UlozitTagSeznamu(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed)),
                message.OnError, save => _repository.UlozitNaradi(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
        }

        private class ZpracovatNaradi
        {
            private SeznamNaradiProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;
            private Guid _naradiId;
            private Action<TypNaradiDto> _akce;
            
            private int _verzeTagu;
            private int _obsahTagu;
            
            public ZpracovatNaradi(SeznamNaradiProjection parent, Action onComplete, Action<Exception> onError, Guid naradiId, Action<TypNaradiDto> akce)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
                _naradiId = naradiId;
                _akce = akce;
            }

            public void Execute()
            {
                _parent._cacheTag.Get("tag", NactenTag, _onError, load => _parent._repository.NacistTagSeznamu(load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenTag(int verzeTagu, int obsahTagu)
            {
                _verzeTagu = verzeTagu;
                _obsahTagu = obsahTagu;

                _parent._cacheNaradi.Get(_naradiId.ToString(), NactenoNaradi, _onError, 
                    load => _parent._repository.NacistNaradi(_naradiId, load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenoNaradi(int verzeNaradi, TypNaradiDto naradi)
            {
                if (naradi == null)
                {
                    naradi = new TypNaradiDto();
                    naradi.Id = _naradiId;
                }
                _akce(naradi);
                _obsahTagu++;
                _parent._cacheNaradi.Insert(_naradiId.ToString(), verzeNaradi, naradi, dirty: true);
                _parent._cacheTag.Insert("tag", _verzeTagu, _obsahTagu, dirty: true);
                _onComplete();
            }


        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            new ZpracovatNaradi(this, message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Vykres = message.Command.Vykres;
                naradi.Rozmer = message.Command.Rozmer;
                naradi.Druh = message.Command.Druh;
                naradi.Aktivni = true;
            }).Execute();
        }

        public void Handle(CommandExecution<AktivovanoNaradiEvent> message)
        {
            new ZpracovatNaradi(this, message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Aktivni = true;
            }).Execute();
        }

        public void Handle(CommandExecution<DeaktivovanoNaradiEvent> message)
        {
            new ZpracovatNaradi(this, message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Aktivni = false;
            }).Execute();
        }
    }

    public class SeznamNaradiRepository
    {
        private IDocumentFolder _folder;

        public SeznamNaradiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistNaradi(Guid naradiId, Action<int, TypNaradiDto> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                naradiId.ToString("N"),
                (verze, raw) => onLoaded(verze, DeserializovatNaradi(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void NacistVsechnoNaradi(Action<List<TypNaradiDto>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("vykresRozmer", null, null, 0, int.MaxValue, true,
                list => onLoaded(list.Select(n => DeserializovatNaradi(n.Contents)).ToList()), onError);
        }

        private TypNaradiDto DeserializovatNaradi(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<TypNaradiDto>(raw);
        }

        public void UlozitNaradi(int verze, TypNaradiDto data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                data.Id.ToString("N"),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                new[] { new DocumentIndexing("vykresRozmer", string.Concat(data.Vykres, " ", data.Rozmer)) },
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistTagSeznamu(Action<int, int> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("tag", (verze, raw) => onLoaded(verze, DeserializovatTag(raw)), () => onLoaded(0, 0), onError);
        }

        private int DeserializovatTag(string raw)
        {
            int tag;
            int.TryParse(raw, out tag);
            return tag;
        }

        public void UlozitTagSeznamu(int verze, int hodnota, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument("tag", hodnota.ToString(), DocumentStoreVersion.At(verze), null,
                () => onSaved(verze + 1), () => onError(new ProjectorMessages.ConcurrencyException()), ex => onError(ex));
        }
    }

    public class SeznamNaradiCachedData
    {
        public int VerzeSeznamu;
        public List<TypNaradiDto> Seznam;
        public int PocetCelkem, PocetStranek;
    }

    public class SeznamNaradiReader
        : IAnswer<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>
        , IAnswer<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>
    {
        private MemoryCache<SeznamNaradiCachedData> _cache;
        private SeznamNaradiRepository _repository;
        private UnikatnostComparer _comparer;

        public SeznamNaradiReader(SeznamNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<SeznamNaradiCachedData>(executor, time);
            _comparer = new UnikatnostComparer();
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>>(this);
            bus.Subscribe<QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>>(this);
        }

        public void Handle(QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse> message)
        {
            _cache.Get("seznam",
                (verze, seznam) => message.OnCompleted(VytvoritResponseSeznamu(message.Request, seznam)),
                ex => message.OnError(ex), load => new NacistCacheWorker(this, load).Execute());
        }

        private ZiskatSeznamNaradiResponse VytvoritResponseSeznamu(ZiskatSeznamNaradiRequest request, SeznamNaradiCachedData seznam)
        {
            return new ZiskatSeznamNaradiResponse
            {
                PocetCelkem = seznam.PocetCelkem,
                PocetStranek = seznam.PocetStranek,
                Stranka = request.Stranka,
                SeznamNaradi = seznam.Seznam.Skip(request.Stranka * 100 - 100).Take(100).ToList()
            };
        }

        public void Handle(QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse> message)
        {
            _cache.Get("seznam",
                (verze, seznam) => message.OnCompleted(VytvoritResponseUnikatnosti(message.Request, seznam)),
                ex => message.OnError(ex), load => new NacistCacheWorker(this, load).Execute());
        }

        private OvereniUnikatnostiResponse VytvoritResponseUnikatnosti(OvereniUnikatnostiRequest request, SeznamNaradiCachedData seznam)
        {
            var vzor = new TypNaradiDto { Vykres = request.Vykres, Rozmer = request.Rozmer };
            var index = seznam.Seznam.BinarySearch(vzor, _comparer);
            return new OvereniUnikatnostiResponse
            {
                Vykres = request.Vykres,
                Rozmer = request.Rozmer,
                Existuje = index >= 0
            };
        }

        private class UnikatnostComparer : IComparer<TypNaradiDto>
        {
            public int Compare(TypNaradiDto x, TypNaradiDto y)
            {
                int compare;
                compare = string.Compare(x.Vykres, y.Vykres);
                if (compare != 0)
                    return compare;
                compare = string.Compare(x.Rozmer, y.Rozmer);
                return compare;
            }
        }

        private class NacistCacheWorker
        {
            private SeznamNaradiReader _parent;
            private IMemoryCacheLoad<SeznamNaradiCachedData> _load;
            private int _verzeSeznamu;

            public NacistCacheWorker(SeznamNaradiReader parent, IMemoryCacheLoad<SeznamNaradiCachedData> load)
            {
                _parent = parent;
                _load = load;
            }

            public void Execute()
            {
                _parent._repository.NacistTagSeznamu(NactenTag, _load.LoadingFailed);
            }

            private void NactenTag(int verzeTagu, int obsahTagu)
            {
                _verzeSeznamu = obsahTagu;
                if (!_load.OldValueAvailable || _load.OldValue == null || _load.OldValue.VerzeSeznamu != _verzeSeznamu)
                {
                    _parent._repository.NacistVsechnoNaradi(NactenoNaradi, _load.LoadingFailed);
                }
                else
                {
                    _load.ValueIsStillValid();
                }
            }

            private void NactenoNaradi(List<TypNaradiDto> seznam)
            {
                if (seznam == null)
                    seznam = new List<TypNaradiDto>();
                var data = new SeznamNaradiCachedData
                {
                    VerzeSeznamu = _verzeSeznamu,
                    Seznam = new List<TypNaradiDto>(seznam),
                    PocetCelkem = seznam.Count,
                    PocetStranek = (seznam.Count + 99) / 100
                };
                data.Seznam.Sort(_parent._comparer);
                _load.SetLoadedValue(_load.OldVersion + 1, data);
            }
        }

    }
}
