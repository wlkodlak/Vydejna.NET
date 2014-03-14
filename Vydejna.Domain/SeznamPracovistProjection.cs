using ServiceLib;
using ServiceStack.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamPracovistProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
    {
        private SeznamPracovistRepository _repository;
        private MemoryCache<InformaceOPracovisti> _cachePracovist;
        private SeznamPracovistDataPracovisteKodComparer _comparer;

        public SeznamPracovistProjection(SeznamPracovistRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _comparer = new SeznamPracovistDataPracovisteKodComparer();
            _cachePracovist = new MemoryCache<InformaceOPracovisti>(executor, time);
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
            _cachePracovist.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            _cachePracovist.Flush(message.OnCompleted, message.OnError, 
                save => _repository.UlozitPracoviste(save.Version, save.Value, save.Value.Aktivni, save.SavedAsVersion, save.SavingFailed));
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            _cachePracovist.Get(
                message.Command.Kod,
                (verze, pracoviste) =>
                {
                    if (pracoviste == null)
                    {
                        pracoviste = new InformaceOPracovisti();
                        pracoviste.Kod = message.Command.Kod;
                    }
                    pracoviste.Nazev = message.Command.Nazev;
                    pracoviste.Stredisko = message.Command.Stredisko;
                    pracoviste.Aktivni = !message.Command.Deaktivovano;

                    _cachePracovist.Insert(message.Command.Kod, verze, pracoviste, dirty: true);
                    message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistPracoviste(message.Command.Kod, load.SetLoadedValue, load.LoadingFailed)
                );
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

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistPracoviste(string kodPracoviste, Action<int, InformaceOPracovisti> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(kodPracoviste,
                (verze, raw) => onLoaded(verze, DeserializovatPracoviste(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        private static InformaceOPracovisti DeserializovatPracoviste(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceOPracovisti>(raw);
        }

        public void UlozitPracoviste(int verze, InformaceOPracovisti data, bool zobrazitVSeznamu, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(data.Kod,
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                zobrazitVSeznamu ? new[] { new DocumentIndexing("kodPracoviste", data.Kod) } : null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistSeznamPracovist(int offset, int pocet, Action<int, List<InformaceOPracovisti>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("kodPracoviste", null, null, offset, pocet, true,
                list => onLoaded(list.TotalFound, list.Select(p => DeserializovatPracoviste(p.Contents)).ToList()),
                ex => onError(ex));
        }
    }

    public class SeznamPracovistReader
           : IAnswer<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>
    {
        private SeznamPracovistRepository _repository;
        private IMemoryCache<ZiskatSeznamPracovistResponse> _cache;

        public SeznamPracovistReader(SeznamPracovistRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatSeznamPracovistResponse>(executor, time);
        }

        public void Handle(QueryExecution<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse> message)
        {
            _cache.Get(
                message.Request.Stranka.ToString(),
                (verze, response) => message.OnCompleted(response),
                ex => message.OnError(ex),
                load => _repository.NacistSeznamPracovist(message.Request.Stranka * 100 - 100, 100,
                    (celkem, seznam) => load.SetLoadedValue(1, VytvoritResponse(message.Request, celkem, seznam)),
                    ex => load.LoadingFailed(ex)));
        }

        private ZiskatSeznamPracovistResponse VytvoritResponse(ZiskatSeznamPracovistRequest request, int celkem, List<InformaceOPracovisti> seznam)
        {
            return new ZiskatSeznamPracovistResponse
            {
                 Stranka = request.Stranka,
                 PocetCelkem = celkem,
                 PocetStranek = (celkem + 99)/100,
                 Seznam = seznam
            };
        }
    }
}
