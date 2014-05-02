using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Projections.SeznamDodavateluReadModel
{
    public class SeznamDodavateluProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
    {
        private SeznamDodavateluRepository _repository;
        private SeznamDodavateluNazevComparer _comparer;
        private MemoryCache<SeznamDodavateluData> _cache;

        public SeznamDodavateluProjection(SeznamDodavateluRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _comparer = new SeznamDodavateluNazevComparer();
            _cache = new MemoryCache<SeznamDodavateluData>(executor, time);
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

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _cache.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            _cache.Flush(message.OnCompleted, message.OnError, save => _repository.UlozitDodavatele(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            _cache.Get("dodavatele", (verze, data) =>
                {
                    InformaceODodavateli dodavatel;
                    if (!data.PodleKodu.TryGetValue(message.Command.Kod, out dodavatel))
                    {
                        dodavatel = new InformaceODodavateli();
                        dodavatel.Kod = message.Command.Kod;
                        data.PodleKodu[dodavatel.Kod] = dodavatel;
                        data.Seznam.Add(dodavatel);
                    }
                    dodavatel.Nazev = message.Command.Nazev;
                    dodavatel.Adresa = message.Command.Adresa;
                    dodavatel.Ico = message.Command.Ico;
                    dodavatel.Dic = message.Command.Dic;
                    dodavatel.Aktivni = !message.Command.Deaktivovan;

                    _cache.Insert("dodavatele", verze, data, dirty: true);
                    message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistDodavatele((verze, data) => load.SetLoadedValue(verze, RozsiritData(data)), load.LoadingFailed));
        }

        private SeznamDodavateluData RozsiritData(SeznamDodavateluData data)
        {
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
            return data;
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

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistDodavatele(Action<int, SeznamDodavateluData> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("dodavatele",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamDodavateluData>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitDodavatele(int verze, SeznamDodavateluData data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument("dodavatele",
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex)
                );
        }
    }

    public class SeznamDodavateluReader
        : IAnswer<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>
    {
        private IMemoryCache<ZiskatSeznamDodavateluResponse> _cache;
        private SeznamDodavateluRepository _repository;

        public SeznamDodavateluReader(SeznamDodavateluRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamDodavateluResponse>(executor, time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<QueryExecution<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>>(this);
        }

        public void Handle(QueryExecution<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse> message)
        {
            _cache.Get("dodavatele", (verze, data) => message.OnCompleted(data), message.OnError,
                load => _repository.NacistDodavatele((verze, data) => load.SetLoadedValue(verze, VytvoritResponse(data)), load.LoadingFailed));
        }

        private ZiskatSeznamDodavateluResponse VytvoritResponse(SeznamDodavateluData zaklad)
        {
            var response = new ZiskatSeznamDodavateluResponse();
            if (zaklad == null)
                response.Seznam = new List<InformaceODodavateli>();
            else
                response.Seznam = zaklad.Seznam.Where(d => d.Aktivni).OrderBy(d => d.Nazev).ToList();
            return response;
        }
    }
}
