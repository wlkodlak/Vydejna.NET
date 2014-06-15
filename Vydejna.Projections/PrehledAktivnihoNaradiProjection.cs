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
        , ISubscribeToEventManager
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<DefinovanoNaradiEvent>
        , IProcessEvent<AktivovanoNaradiEvent>
        , IProcessEvent<DeaktivovanoNaradiEvent>
        , IProcessEvent<ZmenenStavNaSkladeEvent>
        , IProcessEvent<CislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcessEvent<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<CislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<CislovaneNaradiPredanoKeSesrotovaniEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcessEvent<NecislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKeSesrotovaniEvent>
    {
        private const string _version = "0.1";
        private PrehledAktivnihoNaradiRepository _repository;
        private MemoryCache<PrehledAktivnihoNaradiDataNaradi> _cacheNaradi;

        public PrehledAktivnihoNaradiProjection(PrehledAktivnihoNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<PrehledAktivnihoNaradiDataNaradi>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
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
            return _cacheNaradi.Flush(save => _repository.UlozitNaradi(save.Version, save.Value, save.Value.Aktivni));
        }

        private Task ZpracovatZakladniNaradi(Guid naradiId, Action<PrehledAktivnihoNaradiDataNaradi> handler)
        {
            return _cacheNaradi.Get(naradiId.ToString(), load => _repository.NacistNaradi(naradiId)).ContinueWith(task =>
            {
                var naradi = task.Result.Value;
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
                _cacheNaradi.Insert(naradiId.ToString(), task.Result.Version, naradi, dirty: true);
            });
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            return ZpracovatZakladniNaradi(message.NaradiId, naradi =>
            {
                naradi.Vykres = message.Vykres;
                naradi.Rozmer = message.Rozmer;
                naradi.Druh = message.Druh;
                naradi.Aktivni = true;
            });
        }

        public Task Handle(AktivovanoNaradiEvent message)
        {
            return ZpracovatZakladniNaradi(message.NaradiId, naradi =>
            {
                naradi.Aktivni = true;
            });
        }

        public Task Handle(DeaktivovanoNaradiEvent message)
        {
            return ZpracovatZakladniNaradi(message.NaradiId, naradi =>
            {
                naradi.Aktivni = false;
            });
        }

        public Task Handle(ZmenenStavNaSkladeEvent message)
        {
            return ZpracovatZakladniNaradi(message.NaradiId, naradi =>
            {
                naradi.NaSklade = message.NovyStav;
            });
        }

        private Task PresunCislovanehoNaradi(Guid naradiId, int cisloNaradi, UmisteniNaradiDto puvodni, UmisteniNaradiDto nove)
        {
            return ZpracovatZakladniNaradi(naradiId, naradi =>
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
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, null, message.NoveUmisteni);
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni);
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni);
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni);
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, message.NoveUmisteni);
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            return PresunCislovanehoNaradi(message.NaradiId, message.CisloNaradi, message.PredchoziUmisteni, null);
        }

        private Task PresunNecislovanehoNaradi(Guid naradiId, UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, int pocet)
        {
            return ZpracovatZakladniNaradi(naradiId, naradi =>
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
            return PresunNecislovanehoNaradi(message.NaradiId, null, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return PresunNecislovanehoNaradi(message.NaradiId, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return PresunNecislovanehoNaradi(message.NaradiId, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            return PresunNecislovanehoNaradi(message.NaradiId, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            return PresunNecislovanehoNaradi(message.NaradiId, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            return PresunNecislovanehoNaradi(message.NaradiId, message.PredchoziUmisteni, message.NoveUmisteni, message.Pocet);
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

        public Task<MemoryCacheItem<PrehledAktivnihoNaradiDataNaradi>> NacistNaradi(Guid naradiId)
        {
            return _folder.GetDocument(naradiId.ToString("N")).ToMemoryCacheItem(DeserializovatNaradi);
        }

        private static PrehledAktivnihoNaradiDataNaradi DeserializovatNaradi(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledAktivnihoNaradiDataNaradi>(raw);
        }

        public Task<int> UlozitNaradi(int verze, PrehledAktivnihoNaradiDataNaradi data, bool zarazenoDoSeznamu)
        {
            return ProjectorUtils.Save(_folder, data.NaradiId.ToString("N"), verze, JsonSerializer.SerializeToString(data),
                zarazenoDoSeznamu ? new[] { new DocumentIndexing("vykresRozmer", string.Concat(data.Vykres, " ", data.Rozmer)) } : null);
        }

        public Task<Tuple<int, List<PrehledAktivnihoNaradiDataNaradi>>> NacistSeznamNaradi(int offset, int pocet)
        {
            return _folder.FindDocuments("vykresRozmer", null, null, offset, pocet, true)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, task.Result.Select(e => DeserializovatNaradi(e.Contents)).ToList()));
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
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
            return _cache.Get(message.Stranka.ToString(),
                load => _repository.NacistSeznamNaradi(message.Stranka * 100 - 100, 100)
                    .ContinueWith(task => MemoryCacheItem.Create(1, VytvoritResponse(message, task.Result.Item1, task.Result.Item2))))
                    .ExtractValue();
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
