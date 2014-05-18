using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.PrehledAktivnihoNaradiReadModel
{
    public class PrehledAktivnihoNaradiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanoNaradiEvent>
        , IProcess<AktivovanoNaradiEvent>
        , IProcess<DeaktivovanoNaradiEvent>
        , IProcess<ZmenenStavNaSkladeEvent>
        , IProcess<CislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcess<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcess<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcess<CislovaneNaradiPredanoKOpraveEvent>
        , IProcess<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<CislovaneNaradiPredanoKeSesrotovaniEvent>
        , IProcess<NecislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcess<NecislovaneNaradiVydanoDoVyrobyEvent>
        , IProcess<NecislovaneNaradiPrijatoZVyrobyEvent>
        , IProcess<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcess<NecislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<NecislovaneNaradiPredanoKeSesrotovaniEvent>
    {
        private const string _version = "0.1";
        private PrehledAktivnihoNaradiRepository _repository;
        private MemoryCache<PrehledAktivnihoNaradiDataNaradi> _cacheNaradi;

        public PrehledAktivnihoNaradiProjection(PrehledAktivnihoNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<PrehledAktivnihoNaradiDataNaradi>(time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
            mgr.Register<AktivovanoNaradiEvent>(this);
            mgr.Register<DeaktivovanoNaradiEvent>(this);
            mgr.Register<ZmenenStavNaSkladeEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoNaVydejnuEvent>(this);
            mgr.Register<CislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKeSesrotovaniEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoNaVydejnuEvent>(this);
            mgr.Register<NecislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKeSesrotovaniEvent>(this);
        }

        public string GetVersion()
        {
            return _version;
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return (storedVersion == _version) ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public Task Handle(ProjectorMessages.Reset message)
        {
            _cacheNaradi.Clear();
            return _repository.Reset();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(ProjectorMessages.Flush message)
        {
            _cacheNaradi.Flush(message.OnCompleted, message.OnError,
                save => _repository.UlozitNaradi(save.Version, save.Value, save.Value.Aktivni));
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
                        naradi.CislovaneOpravovane = new HashSet<int>();
                        naradi.CislovanePoskozene = new HashSet<int>();
                        naradi.CislovaneVeVyrobe = new HashSet<int>();
                        naradi.CislovaneVPoradku = new HashSet<int>();
                        naradi.CislovaneZnicene = new HashSet<int>();
                    }
                    handler(naradi);
                    _cacheNaradi.Insert(naradiId.ToString(), verze, naradi, dirty: true);
                    onComplete();
                },
                ex => onError(ex),
                load => _repository.NacistNaradi(naradiId, load.SetLoadedValue, load.LoadingFailed));
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.NaradiId, naradi =>
            {
                naradi.Vykres = message.Vykres;
                naradi.Rozmer = message.Rozmer;
                naradi.Druh = message.Druh;
                naradi.Aktivni = true;
            });
        }

        public Task Handle(AktivovanoNaradiEvent message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.NaradiId, naradi =>
            {
                naradi.Aktivni = true;
            });
        }

        public Task Handle(DeaktivovanoNaradiEvent message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.NaradiId, naradi =>
            {
                naradi.Aktivni = false;
            });
        }

        public Task Handle(ZmenenStavNaSkladeEvent message)
        {
            ZpracovatZakladniNaradi(message.OnCompleted, message.OnError, message.NaradiId, naradi =>
            {
                naradi.NaSklade = message.NovyStav;
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

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, null, message.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni, message.OnCompleted, message.OnError);
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, null, message.OnCompleted, message.OnError);
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

        public Task Handle(NecislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, null, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            PresunNecislovanehoNaradi(message.NaradiId, message.OnCompleted, message.OnError, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
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

        public PrehledAktivnihoNaradiReader(PrehledAktivnihoNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<PrehledNaradiResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<PrehledNaradiRequest, PrehledNaradiResponse>(this);
        }

        public Task<PrehledNaradiResponse> Handle(PrehledNaradiRequest message)
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
