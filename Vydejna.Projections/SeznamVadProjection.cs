using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Projections.SeznamVadReadModel
{
    public class SeznamVadProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanaVadaNaradiEvent>
    {
        private SeznamVadRepository _repository;
        private MemoryCache<SeznamVadData> _cache;
        private IComparer<SeznamVadPolozka> _comparer;

        public SeznamVadProjection(SeznamVadRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<SeznamVadData>(time);
            _comparer = new SeznamVadKodComparer();
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanaVadaNaradiEvent>(this);
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
            _cache.Clear();
            return _repository.Reset();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(ProjectorMessages.Flush message)
        {
            return _cache.Flush(save => _repository.UlozitVady(save.Version, save.Value));
        }

        public Task Handle(DefinovanaVadaNaradiEvent message)
        {
            return _cache.Get("vady", load => _repository.NacistVady().ContinueWith(task => RozsiritData(task.Result))).ContinueWith(task =>
            {
                var verze = task.Result.Version;
                var seznamVad = task.Result.Value;
                var vzor = new SeznamVadPolozka { Kod = message.Kod };
                var index = seznamVad.Seznam.BinarySearch(vzor, _comparer);
                SeznamVadPolozka vada;
                if (index >= 0)
                {
                    vada = seznamVad.Seznam[index];
                }
                else
                {
                    vada = new SeznamVadPolozka();
                    vada.Kod = message.Kod;
                    seznamVad.Seznam.Insert(~index, vada);
                }
                vada.Nazev = message.Nazev;
                vada.Aktivni = !message.Deaktivovana;

                _cache.Insert("vady", verze, seznamVad, dirty: true);
            });
        }

        private MemoryCacheItem<SeznamVadData> RozsiritData(MemoryCacheItem<SeznamVadData> item)
        {
            var data = item.Value;
            if (data == null)
            {
                data = new SeznamVadData();
                data.Seznam = new List<SeznamVadPolozka>();
            }
            return new MemoryCacheItem<SeznamVadData>(item.Version, data);
        }
    }

    public class SeznamVadData
    {
        public List<SeznamVadPolozka> Seznam { get; set; }
    }

    public class SeznamVadRepository
    {
        private IDocumentFolder _folder;

        public SeznamVadRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<SeznamVadData>> NacistVady()
        {
            return _folder.GetDocument("vady").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<SeznamVadData>);
        }

        public Task<int> UlozitVady(int verze, SeznamVadData data)
        {
            return ProjectorUtils.Save(_folder, "vady", verze, JsonSerializer.SerializeToString(data), null);
        }
    }

    public class SeznamVadReader
        : IAnswer<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>
    {
        private SeznamVadRepository _repository;
        private IMemoryCache<ZiskatSeznamVadResponse> _cache;

        public SeznamVadReader(SeznamVadRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamVadResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>(this);
        }

        public Task<ZiskatSeznamVadResponse> Handle(ZiskatSeznamVadRequest message)
        {
            return _cache.Get("vady", load => _repository.NacistVady().Transform(VytvoritResponse)).ExtractValue();
        }

        private ZiskatSeznamVadResponse VytvoritResponse(SeznamVadData zaklad)
        {
            var response = new ZiskatSeznamVadResponse();
            if (zaklad == null)
                response.Seznam = new List<SeznamVadPolozka>();
            else
                response.Seznam = zaklad.Seznam.Where(v => v.Aktivni).ToList();
            return response;
        }
    }

    public class SeznamVadKodComparer : IComparer<SeznamVadPolozka>
    {
        public int Compare(SeznamVadPolozka x, SeznamVadPolozka y)
        {
            return string.CompareOrdinal(x.Kod, y.Kod);
        }
    }
}
