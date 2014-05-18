using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.SeznamNaradiReadModel
{
    public class SeznamNaradiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanoNaradiEvent>
        , IProcess<AktivovanoNaradiEvent>
        , IProcess<DeaktivovanoNaradiEvent>
    {
        private SeznamNaradiRepository _repository;
        private MemoryCache<TypNaradiDto> _cacheNaradi;
        private MemoryCache<int> _cacheTag;

        public SeznamNaradiProjection(SeznamNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<TypNaradiDto>(time);
            _cacheTag = new MemoryCache<int>(time);
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

        public Task Handle(ProjectorMessages.Reset message)
        {
            _cacheTag.Clear();
            _cacheNaradi.Clear();
            return _repository.Reset();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(ProjectorMessages.Flush message)
        {
            return TaskUtils.FromEnumerable(FlushInternal()).GetTask();
        }

        private IEnumerable<Task> FlushInternal()
        {
            var taskNaradi = _cacheNaradi.Flush(save => _repository.UlozitNaradi(save.Version, save.Value));
            yield return taskNaradi;
            taskNaradi.Wait();

            var taskTag = _cacheTag.Flush(save => _repository.UlozitTagSeznamu(save.Version, save.Value));
            yield return taskNaradi;
            taskNaradi.Wait();
        }

        private IEnumerable<Task> ZpracovatNaradi(Guid naradiId, Action<TypNaradiDto> akce)
        {
            var taskTag = _cacheTag.Get("tag", load => _repository.NacistTagSeznamu());
            yield return taskTag;
            var verzeTagu = taskTag.Result.Version;
            var obsahTagu = taskTag.Result.Value;

            var taskNaradi = _cacheNaradi.Get(naradiId.ToString(), load => _repository.NacistNaradi(naradiId));
            yield return taskNaradi;
            var verzeNaradi = taskNaradi.Result.Version;
            var naradi = taskNaradi.Result.Value;

            if (naradi == null)
            {
                naradi = new TypNaradiDto();
                naradi.Id = naradiId;
            }
            akce(naradi);
            obsahTagu++;
            
            _cacheNaradi.Insert(naradiId.ToString(), verzeNaradi, naradi, dirty: true);
            _cacheTag.Insert("tag", verzeTagu, obsahTagu, dirty: true);
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatNaradi(message.NaradiId, naradi =>
            {
                naradi.Vykres = message.Vykres;
                naradi.Rozmer = message.Rozmer;
                naradi.Druh = message.Druh;
                naradi.Aktivni = true;
            })).GetTask();
        }

        public Task Handle(AktivovanoNaradiEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatNaradi(message.NaradiId, naradi =>
            {
                naradi.Aktivni = true;
            })).GetTask();
        }

        public Task Handle(DeaktivovanoNaradiEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatNaradi(message.NaradiId, naradi =>
            {
                naradi.Aktivni = false;
            })).GetTask();
        }
    }

    public class SeznamNaradiRepository
    {
        private IDocumentFolder _folder;

        public SeznamNaradiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<TypNaradiDto>> NacistNaradi(Guid naradiId)
        {
            return _folder.GetDocument(naradiId.ToString("N")).ContinueWith(task => ProjectorUtils.LoadFromDocument<TypNaradiDto>(task, DeserializovatNaradi)).Unwrap();
        }

        public Task<List<TypNaradiDto>> NacistVsechnoNaradi()
        {
            return _folder.FindDocuments("vykresRozmer", null, null, 0, int.MaxValue, true)
                .ContinueWith(task => task.Result.Select(n => DeserializovatNaradi(n.Contents)).ToList());
        }

        private TypNaradiDto DeserializovatNaradi(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<TypNaradiDto>(raw);
        }

        public Task<int> UlozitNaradi(int verze, TypNaradiDto data)
        {
            return _folder.SaveDocument(
                data.Id.ToString("N"),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                new[] { new DocumentIndexing("vykresRozmer", string.Concat(data.Vykres, " ", data.Rozmer)) })
                .ContinueWith(task => ProjectorUtils.CheckConcurrency(task, verze + 1)).Unwrap();
        }

        public Task<MemoryCacheItem<int>> NacistTagSeznamu()
        {
            return _folder.GetDocument("tag").ContinueWith(task => ProjectorUtils.LoadFromDocument<int>(task, DeserializovatTag)).Unwrap();
        }

        private int DeserializovatTag(string raw)
        {
            int tag;
            int.TryParse(raw, out tag);
            return tag;
        }

        public Task<int> UlozitTagSeznamu(int verze, int hodnota)
        {
            return _folder.SaveDocument("tag", hodnota.ToString(), DocumentStoreVersion.At(verze), null)
                .ContinueWith(Task => ProjectorUtils.CheckConcurrency(Task, verze + 1)).Unwrap();
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

        public SeznamNaradiReader(SeznamNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<SeznamNaradiCachedData>(time);
            _comparer = new UnikatnostComparer();
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(this);
            bus.Subscribe<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(this);
        }

        public Task<ZiskatSeznamNaradiResponse> Handle(ZiskatSeznamNaradiRequest message)
        {
            return _cache.Get("seznam", load => TaskUtils.FromEnumerable<MemoryCacheItem<SeznamNaradiCachedData>>(NacistCacheInternal(load)).GetTask())
                .ContinueWith(task => VytvoritResponseSeznamu(message, task.Result));
        }

        private ZiskatSeznamNaradiResponse VytvoritResponseSeznamu(ZiskatSeznamNaradiRequest request, MemoryCacheItem<SeznamNaradiCachedData> item)
        {
            var seznam = item.Value;
            return new ZiskatSeznamNaradiResponse
            {
                PocetCelkem = seznam.PocetCelkem,
                PocetStranek = seznam.PocetStranek,
                Stranka = request.Stranka,
                SeznamNaradi = seznam.Seznam.Skip(request.Stranka * 100 - 100).Take(100).ToList()
            };
        }

        public Task<OvereniUnikatnostiResponse> Handle(OvereniUnikatnostiRequest message)
        {
            return _cache.Get("seznam", load => TaskUtils.FromEnumerable<MemoryCacheItem<SeznamNaradiCachedData>>(NacistCacheInternal(load)).GetTask())
                .ContinueWith(task => VytvoritResponseUnikatnosti(message, task.Result));
        }

        private OvereniUnikatnostiResponse VytvoritResponseUnikatnosti(OvereniUnikatnostiRequest request, MemoryCacheItem<SeznamNaradiCachedData> item)
        {
            var seznam = item.Value;
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

        private IEnumerable<Task> NacistCacheInternal(IMemoryCacheLoad<SeznamNaradiCachedData> load)
        {
            var taskTag = _repository.NacistTagSeznamu();
            yield return taskTag;
            var tag = taskTag.Result.Value;

            if (!load.OldValueAvailable || load.OldValue == null || load.OldValue.VerzeSeznamu != tag)
            {
                var taskSeznam = _repository.NacistVsechnoNaradi();
                yield return taskSeznam;
                var seznam = taskSeznam.Result;

                if (seznam == null)
                    seznam = new List<TypNaradiDto>();
                var data = new SeznamNaradiCachedData
                {
                    VerzeSeznamu = tag,
                    Seznam = new List<TypNaradiDto>(seznam),
                    PocetCelkem = seznam.Count,
                    PocetStranek = (seznam.Count + 99) / 100
                };
                data.Seznam.Sort(_comparer);

                yield return TaskUtils.FromResult(new MemoryCacheItem<SeznamNaradiCachedData>(load.OldVersion + 1, data));
            }
            else
            {
                yield return TaskUtils.FromResult<MemoryCacheItem<SeznamNaradiCachedData>>(null);
            }
        }
    }
}
