﻿using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Projections.SeznamDodavateluReadModel
{
    public class SeznamDodavateluProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanDodavatelEvent>
    {
        private SeznamDodavateluRepository _repository;
        private SeznamDodavateluNazevComparer _comparer;
        private MemoryCache<SeznamDodavateluData> _cache;

        public SeznamDodavateluProjection(SeznamDodavateluRepository repository, ITime time)
        {
            _repository = repository;
            _comparer = new SeznamDodavateluNazevComparer();
            _cache = new MemoryCache<SeznamDodavateluData>(time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanDodavatelEvent>(this);
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
            return _cache.Flush(save => _repository.UlozitDodavatele(save.Version, save.Value));
        }

        public Task Handle(DefinovanDodavatelEvent message)
        {
            return _cache.Get("dodavatele", load => _repository.NacistDodavatele().ContinueWith(task => RozsiritData(task.Result))).ContinueWith(task =>
            {
                var data = task.Result.Value;
                InformaceODodavateli dodavatel;
                if (!data.PodleKodu.TryGetValue(message.Kod, out dodavatel))
                {
                    dodavatel = new InformaceODodavateli();
                    dodavatel.Kod = message.Kod;
                    data.PodleKodu[dodavatel.Kod] = dodavatel;
                    data.Seznam.Add(dodavatel);
                }
                dodavatel.Nazev = message.Nazev;
                dodavatel.Adresa = message.Adresa;
                dodavatel.Ico = message.Ico;
                dodavatel.Dic = message.Dic;
                dodavatel.Aktivni = !message.Deaktivovan;

                _cache.Insert("dodavatele", task.Result.Version, data, dirty: true);
            });
        }

        private MemoryCacheItem<SeznamDodavateluData> RozsiritData(MemoryCacheItem<SeznamDodavateluData> item)
        {
            var data = item.Value;
            if (data == null)
            {
                data = new SeznamDodavateluData();
                data.Seznam = new List<InformaceODodavateli>();
                data.PodleKodu = new Dictionary<string, InformaceODodavateli>();
            }
            else if (data.PodleKodu == null)
            {
                data.PodleKodu = new Dictionary<string, InformaceODodavateli>(data.Seznam.Count);
                foreach (var dodavatel in data.Seznam)
                    data.PodleKodu[dodavatel.Kod] = dodavatel;
            }
            return new MemoryCacheItem<SeznamDodavateluData>(item.Version, data);
        }
    }

    public class SeznamDodavateluData
    {
        public List<InformaceODodavateli> Seznam { get; set; }
        public Dictionary<string, InformaceODodavateli> PodleKodu;
    }
    public class SeznamDodavateluNazevComparer
        : IComparer<InformaceODodavateli>
    {
        public int Compare(InformaceODodavateli x, InformaceODodavateli y)
        {
            return string.Compare(x.Nazev, y.Nazev);
        }
    }

    public class SeznamDodavateluRepository
    {
        private IDocumentFolder _folder;

        public SeznamDodavateluRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<SeznamDodavateluData>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele")
                .ContinueWith(task => ProjectorUtils.LoadFromDocument<SeznamDodavateluData>(task, JsonSerializer.DeserializeFromString<SeznamDodavateluData>)).Unwrap();
        }

        public Task<int> UlozitDodavatele(int verze, SeznamDodavateluData data)
        {
            return _folder.SaveDocument("dodavatele", JsonSerializer.SerializeToString(data), DocumentStoreVersion.At(verze), null)
                .ContinueWith(task => ProjectorUtils.CheckConcurrency(task, verze + 1)).Unwrap();
        }
    }

    public class SeznamDodavateluReader
        : IAnswer<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>
    {
        private IMemoryCache<ZiskatSeznamDodavateluResponse> _cache;
        private SeznamDodavateluRepository _repository;

        public SeznamDodavateluReader(SeznamDodavateluRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamDodavateluResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>(this);
        }

        public Task<ZiskatSeznamDodavateluResponse> Handle(ZiskatSeznamDodavateluRequest message)
        {
            return _cache.Get("dodavatele", load => _repository.NacistDodavatele()
                    .ContinueWith(task => VytvoritResponse(task.Result)))
                .ContinueWith(task => task.Result.Value);
        }

        private MemoryCacheItem<ZiskatSeznamDodavateluResponse> VytvoritResponse(MemoryCacheItem<SeznamDodavateluData> zaklad)
        {
            var response = new ZiskatSeznamDodavateluResponse();
            if (zaklad == null)
                response.Seznam = new List<InformaceODodavateli>();
            else
                response.Seznam = zaklad.Value.Seznam.Where(d => d.Aktivni).OrderBy(d => d.Nazev).ToList();
            return new MemoryCacheItem<ZiskatSeznamDodavateluResponse>(zaklad.Version, response);
        }
    }
}
