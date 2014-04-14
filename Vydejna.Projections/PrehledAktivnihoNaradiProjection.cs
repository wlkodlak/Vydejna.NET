using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Projections.PrehledAktivnihoNaradiReadModel
{
    public class PrehledAktivnihoNaradiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<AktivovanoNaradiEvent>>
        , IHandle<CommandExecution<DeaktivovanoNaradiEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent>>
        , IHandle<CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent>>
    {
        private const string _version = "0.1";
        private PrehledAktivnihoNaradiRepository _repository;
        private MemoryCache<PrehledAktivnihoNaradiDataNaradi> _cacheNaradi;

        public PrehledAktivnihoNaradiProjection(PrehledAktivnihoNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<PrehledAktivnihoNaradiDataNaradi>(executor, time);
        }

        public string GetVersion()
        {
            return _version;
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return (storedVersion == _version) ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _cacheNaradi.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            _cacheNaradi.Flush(message.OnCompleted, message.OnError, 
                save => _repository.UlozitNaradi(save.Version, save.Value, save.Value.Aktivni, save.SavedAsVersion, save.SavingFailed));
        }

        private void ZpracovatZakladniNaradi(Action onComplete, Action<Exception> onError, Guid naradiId, Action<PrehledAktivnihoNaradiDataNaradi> handler)
        {
            _cacheNaradi.Get(naradiId.ToString(),
                (verze, naradi) =>
                {
                    if (naradi == null)
                    {
                        naradi = new PrehledAktivnihoNaradiDataNaradi();
                        naradi.NaradiId = naradiId;
                    }
                    handler(naradi);
                    _cacheNaradi.Insert(naradiId.ToString(), verze, naradi, dirty: true);
                    onComplete();
                },
                ex => onError(ex),
                load => _repository.NacistNaradi(naradiId, load.SetLoadedValue, load.LoadingFailed));
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Vykres = message.Command.Vykres;
                naradi.Rozmer = message.Command.Rozmer;
                naradi.Druh = message.Command.Druh;
            });
        }

        public void Handle(CommandExecution<AktivovanoNaradiEvent> message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<DeaktivovanoNaradiEvent> message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, naradi =>
            {
                naradi.Aktivni = false;
            });
        }

        private void PresunCislovanehoNaradi(Guid naradiId, int cisloNaradi, UmisteniNaradiDto puvodni, UmisteniNaradiDto nove, Action onCompleted, Action<Exception> onError)
        {
            ZpracovatZakladniNaradi(onCompleted, onError, naradiId, naradi =>
            {
                if (puvodni != null)
                    SeznamCislovanehoNaradiNaUmisteni(naradi, puvodni).Remove(cisloNaradi);
                if (nove != null)
                    SeznamCislovanehoNaradiNaUmisteni(naradi, nove).Add(cisloNaradi);
            });
        }

        private HashSet<int> SeznamCislovanehoNaradiNaUmisteni(PrehledAktivnihoNaradiDataNaradi naradi, UmisteniNaradiDto umisteni)
        {
            if (umisteni == null)
                return new HashSet<int>();
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            return naradi.CislovaneVPoradku;
                        case "NutnoOpravit":
                            return naradi.CislovanePoskozene;
                        case "Neopravitelne":
                            return naradi.CislovaneZnicene;
                        default:
                            return new HashSet<int>();
                    }
                case ZakladUmisteni.VOprave:
                    return naradi.CislovaneOpravovane;
                case ZakladUmisteni.VeVyrobe:
                    return naradi.CislovaneVeVyrobe;
                default:
                    return new HashSet<int>();
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, null, message.Command.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, null, message.OnCompleted, message.OnError);
        }

        private void PresunNecislovanehoNaradi(Guid naradiId, Action onCompleted, Action<Exception> onError, UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, int pocet)
        {
            ZpracovatZakladniNaradi(onCompleted, onError, naradiId, naradi =>
            {
                UpravitPocetNaNecislovanemUmisteni(naradi, predchozi, -pocet);
                UpravitPocetNaNecislovanemUmisteni(naradi, nove, pocet);
            });
        }

        private static void UpravitPocetNaNecislovanemUmisteni(PrehledAktivnihoNaradiDataNaradi naradi, UmisteniNaradiDto umisteni, int zmenaPoctu)
        {
            if (umisteni == null)
                return;
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            naradi.NecislovaneVPoradku += zmenaPoctu;
                            break;
                        case "NutnoOpravit":
                            naradi.NecislovanePoskozene += zmenaPoctu;
                            break;
                        case "Neopravitelne":
                            naradi.NecislovaneZnicene += zmenaPoctu;
                            break;
                    }
                    break;
                case ZakladUmisteni.VeVyrobe:
                    naradi.NecislovaneVeVyrobe += zmenaPoctu;
                    break;
                case ZakladUmisteni.VOprave:
                    naradi.NecislovaneOpravovane += zmenaPoctu;
                    break;
            }
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, null, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.OnError, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }
    }

    public class PrehledAktivnihoNaradiDataNaradi
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }
        public int NaSklade { get; set; }
        public int NecislovaneVPoradku { get; set; }
        public int NecislovaneVeVyrobe { get; set; }
        public int NecislovanePoskozene { get; set; }
        public int NecislovaneZnicene { get; set; }
        public int NecislovaneOpravovane { get; set; }
        public HashSet<int> CislovaneVPoradku { get; set; }
        public HashSet<int> CislovaneVeVyrobe { get; set; }
        public HashSet<int> CislovanePoskozene { get; set; }
        public HashSet<int> CislovaneZnicene { get; set; }
        public HashSet<int> CislovaneOpravovane { get; set; }
    }

    public class PrehledAktivnihoNaradiRepository
    {
        private IDocumentFolder _folder;

        public PrehledAktivnihoNaradiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void NacistNaradi(Guid naradiId, Action<int, PrehledAktivnihoNaradiDataNaradi> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                naradiId.ToString("N"),
                (verze, raw) => onLoaded(verze, DeserializovatNaradi(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        private static PrehledAktivnihoNaradiDataNaradi DeserializovatNaradi(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledAktivnihoNaradiDataNaradi>(raw);
        }

        public void UlozitNaradi(int verze, PrehledAktivnihoNaradiDataNaradi data, bool zarazenoDoSeznamu, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                data.NaradiId.ToString("N"),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                zarazenoDoSeznamu ? new[] { new DocumentIndexing("vykresRozmer", string.Concat(data.Vykres, " ", data.Rozmer)) } : null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistSeznamNaradi(int offset, int pocet, Action<int, List<PrehledAktivnihoNaradiDataNaradi>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("vykresRozmer", null, null, offset, pocet, true,
                list => onLoaded(list.TotalFound, list.Select(e => DeserializovatNaradi(e.Contents)).ToList()),
                ex => onError(ex));
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }
    }

    public class PrehledAktivnihoNaradiReader
        : IAnswer<PrehledNaradiRequest, PrehledNaradiResponse>
    {
        private MemoryCache<PrehledNaradiResponse> _cache;
        private PrehledAktivnihoNaradiRepository _repository;

        public PrehledAktivnihoNaradiReader(PrehledAktivnihoNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<PrehledNaradiResponse>(executor, time);
        }

        public void Handle(QueryExecution<PrehledNaradiRequest, PrehledNaradiResponse> message)
        {
            _cache.Get(
                message.Request.Stranka.ToString(),
                (verze, response) => message.OnCompleted(response),
                ex => message.OnError(ex),
                load => _repository.NacistSeznamNaradi(
                    message.Request.Stranka * 100 - 100, 100, 
                    (celkem, seznam) => load.SetLoadedValue(1, VytvoritResponse(message.Request, celkem, seznam)),
                    load.LoadingFailed)
                );
        }

        private PrehledNaradiResponse VytvoritResponse(PrehledNaradiRequest request, int celkem, List<PrehledAktivnihoNaradiDataNaradi> seznam)
        {
            return new PrehledNaradiResponse
            {
                PocetCelkem = celkem,
                PocetStranek = (celkem + 99) / 100,
                Stranka = request.Stranka,
                Naradi = seznam.Select(KonverzeNaResponse).ToList()
            };
        }

        private PrehledNaradiPolozka KonverzeNaResponse(PrehledAktivnihoNaradiDataNaradi data)
        {
            return new PrehledNaradiPolozka
            {
                NaradiId = data.NaradiId,
                Vykres = data.Vykres,
                Rozmer = data.Rozmer,
                Druh = data.Druh,
                NaSklade = data.NaSklade,
                VPoradku = data.NecislovaneVPoradku + data.CislovaneVPoradku.Count,
                VeVyrobe = data.NecislovaneVeVyrobe + data.CislovaneVeVyrobe.Count,
                Poskozene = data.NecislovanePoskozene + data.CislovanePoskozene.Count,
                Opravovane = data.NecislovaneOpravovane + data.CislovaneOpravovane.Count,
                Znicene = data.NecislovaneZnicene + data.CislovaneZnicene.Count
            };
        }
    }

}
