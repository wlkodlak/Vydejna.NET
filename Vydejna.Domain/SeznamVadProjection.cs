using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamVadProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanaVadaNaradiEvent>>
    {
        private SeznamVadRepository _repository;
        private MemoryCache<SeznamVadData> _cache;
        private IComparer<SeznamVadPolozka> _comparer;

        public SeznamVadProjection(SeznamVadRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<SeznamVadData>(executor, time);
            _comparer = new SeznamVadKodComparer();
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
            _cache.Flush(message.OnCompleted, message.OnError, save => _repository.UlozitVady(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
        }

        public void Handle(CommandExecution<DefinovanaVadaNaradiEvent> message)
        {
            _cache.Get("vady", (verze, seznamVad) =>
                {
                    var vzor = new SeznamVadPolozka { Kod = message.Command.Kod };
                    var index = seznamVad.Seznam.BinarySearch(vzor, _comparer);
                    SeznamVadPolozka vada;
                    if (index >= 0)
                    {
                        vada = seznamVad.Seznam[index];
                    }
                    else
                    {
                        vada = new SeznamVadPolozka();
                        vada.Kod = message.Command.Kod;
                        seznamVad.Seznam.Insert(~index, vada);
                    }
                    vada.Nazev = message.Command.Nazev;
                    vada.Aktivni = !message.Command.Deaktivovana;

                    _cache.Insert("vady", verze, seznamVad, dirty: true);
                    message.OnCompleted();
                }, 
                message.OnError, 
                load => _repository.NacistVady((verze, data) => load.SetLoadedValue(verze, RozsiritData(data)), load.LoadingFailed));
        }

        private SeznamVadData RozsiritData(SeznamVadData data)
        {
            if (data == null)
            {
                data = new SeznamVadData();
                data.Seznam = new List<SeznamVadPolozka>();
            }
            return data;
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

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistVady(Action<int, SeznamVadData> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("vady",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamVadData>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitVady(int verze, SeznamVadData data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument("vady",
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex)
                );
        }
    }

    public class SeznamVadReader
        : IAnswer<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>
    {
        private SeznamVadRepository _repository;
        private IMemoryCache<ZiskatSeznamVadResponse> _cache;

        public SeznamVadReader(SeznamVadRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamVadResponse>(executor, time);
        }

        public void Handle(QueryExecution<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse> message)
        {
            _cache.Get("vady", (verze, data) => message.OnCompleted(data), message.OnError,
                load => _repository.NacistVady((verze, data) => load.SetLoadedValue(verze, VytvoritResponse(data)), load.LoadingFailed));
        }

        private ZiskatSeznamVadResponse VytvoritResponse(SeznamVadData zaklad)
        {
            var response = new ZiskatSeznamVadResponse();
            if (zaklad == null)
                response.Seznam = new List<SeznamVadPolozka>();
            else
                response.Seznam = zaklad.Seznam;
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
