using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Projections.SeznamPracovistReadModel
{
    public class SeznamPracovistProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<DefinovanoPracovisteEvent>
    {
        private SeznamPracovistRepository _repository;
        private MemoryCache<InformaceOPracovisti> _cachePracovist;
        private SeznamPracovistDataPracovisteKodComparer _comparer;

        public SeznamPracovistProjection(SeznamPracovistRepository repository, ITime time)
        {
            _repository = repository;
            _comparer = new SeznamPracovistDataPracovisteKodComparer();
            _cachePracovist = new MemoryCache<InformaceOPracovisti>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoPracovisteEvent>(this);
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return storedVersion == GetVersion() ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public Task Handle(ProjectorMessages.Reset message)
        {
            _cachePracovist.Clear();
            return _repository.Reset();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(ProjectorMessages.Flush message)
        {
            return _cachePracovist.Flush(save => _repository.UlozitPracoviste(save.Version, save.Value, save.Value.Aktivni));
        }

        public Task Handle(DefinovanoPracovisteEvent message)
        {
            return _cachePracovist.Get(message.Kod, load => _repository.NacistPracoviste(message.Kod)).ContinueWith(task =>
            {
                var pracoviste = task.Result.Value;
                if (pracoviste == null)
                {
                    pracoviste = new InformaceOPracovisti();
                    pracoviste.Kod = message.Kod;
                }
                pracoviste.Nazev = message.Nazev;
                pracoviste.Stredisko = message.Stredisko;
                pracoviste.Aktivni = !message.Deaktivovano;

                _cachePracovist.Insert(message.Kod, task.Result.Version, pracoviste, dirty: true);
            });
        }
    }

    public class SeznamPracovistDataPracovisteKodComparer
        : IComparer<InformaceOPracovisti>
    {
        public int Compare(InformaceOPracovisti x, InformaceOPracovisti y)
        {
            return string.CompareOrdinal(x.Kod, y.Kod);
        }
    }

    public class SeznamPracovistRepository
    {
        private IDocumentFolder _folder;

        public SeznamPracovistRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<InformaceOPracovisti>> NacistPracoviste(string kodPracoviste)
        {
            return _folder.GetDocument(kodPracoviste).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceOPracovisti>);
        }

        private static InformaceOPracovisti DeserializovatPracoviste(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceOPracovisti>(raw);
        }

        public Task<int> UlozitPracoviste(int verze, InformaceOPracovisti data, bool zobrazitVSeznamu)
        {
            return EventProjectorUtils.Save(_folder, data.Kod, verze, JsonSerializer.SerializeToString(data),
                zobrazitVSeznamu ? new[] { new DocumentIndexing("kodPracoviste", data.Kod) } : null);
        }

        public Task<Tuple<int, List<InformaceOPracovisti>>> NacistSeznamPracovist(int offset, int pocet)
        {
            return _folder.FindDocuments("kodPracoviste", null, null, offset, pocet, true)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, task.Result.Select(p => DeserializovatPracoviste(p.Contents)).ToList()));
        }
    }

    public class SeznamPracovistReader
           : IAnswer<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>
    {
        private SeznamPracovistRepository _repository;
        private IMemoryCache<ZiskatSeznamPracovistResponse> _cache;

        public SeznamPracovistReader(SeznamPracovistRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamPracovistResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>(this);
        }

        public Task<ZiskatSeznamPracovistResponse> Handle(ZiskatSeznamPracovistRequest message)
        {
            return _cache.Get(message.Stranka.ToString(), 
                load => _repository.NacistSeznamPracovist(message.Stranka * 100 - 100, 100)
                    .ContinueWith(task => MemoryCacheItem.Create(1, VytvoritResponse(message, task.Result.Item1, task.Result.Item2))))
                .ContinueWith(task => task.Result.Value);
        }

        private ZiskatSeznamPracovistResponse VytvoritResponse(ZiskatSeznamPracovistRequest request, int celkem, List<InformaceOPracovisti> seznam)
        {
            return new ZiskatSeznamPracovistResponse
            {
                Stranka = request.Stranka,
                PocetCelkem = celkem,
                PocetStranek = (celkem + 99) / 100,
                Seznam = seznam
            };
        }
    }
}
