using ServiceLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;
using System.Threading.Tasks;

namespace Vydejna.Projections.PrehledObjednavekReadModel
{
    public class PrehledObjednavekProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<DefinovanDodavatelEvent>
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<CislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZOpravyEvent>
    {
        private PrehledObjednavekRepository _repository;
        private MemoryCache<int> _cacheVerze;
        private MemoryCache<PrehledObjednavekDataObjednavky> _cacheObjednavek;
        private MemoryCache<PrehledObjednavekDataSeznamDodavatelu> _cacheDodavatelu;

        public PrehledObjednavekProjection(PrehledObjednavekRepository repository, ITime time)
        {
            _repository = repository;
            _cacheVerze = new MemoryCache<int>(time);
            _cacheObjednavek = new MemoryCache<PrehledObjednavekDataObjednavky>(time);
            _cacheDodavatelu = new MemoryCache<PrehledObjednavekDataSeznamDodavatelu>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<DefinovanDodavatelEvent>(this);
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZOpravyEvent>(this);
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
            _cacheDodavatelu.Clear();
            _cacheObjednavek.Clear();
            _cacheVerze.Clear();
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
            var taskDodavatele = _cacheDodavatelu.Flush(save => _repository.UlozitDodavatele(save.Version, save.Value));
            yield return taskDodavatele;
            taskDodavatele.Wait();

            var taskObjednavky = _cacheObjednavek.Flush(save => _repository.UlozitObjednavku(save.Version, save.Value));
            yield return taskObjednavky;
            taskObjednavky.Wait();

            var taskVerze = _cacheVerze.Flush(save => _repository.UlozitVerziSeznamu(save.Version, save.Value));
            yield return taskVerze;
            taskVerze.Wait();
        }

        public Task Handle(DefinovanDodavatelEvent message)
        {
            return _cacheDodavatelu.Get("dodavatele", NacistDodavatele)
                .ContinueWith(task => ZpracovatDefiniciDodavatele(task.Result.Version, task.Result.Value, message));
        }

        private Task<MemoryCacheItem<PrehledObjednavekDataSeznamDodavatelu>> NacistDodavatele(IMemoryCacheLoad<PrehledObjednavekDataSeznamDodavatelu> load)
        {
            return _repository.NacistDodavatele().Transform(RozsiritData);
        }

        private PrehledObjednavekDataSeznamDodavatelu RozsiritData(PrehledObjednavekDataSeznamDodavatelu dodavatele)
        {
            if (dodavatele == null)
            {
                dodavatele = new PrehledObjednavekDataSeznamDodavatelu();
                dodavatele.SeznamDodavatelu = new List<PrehledObjednavekDataDodavatele>();
                dodavatele.IndexDodavatelu = new Dictionary<string, PrehledObjednavekDataDodavatele>();
            }
            else if (dodavatele.IndexDodavatelu == null)
            {
                dodavatele.IndexDodavatelu = new Dictionary<string, PrehledObjednavekDataDodavatele>();
                foreach (var dodavatel in dodavatele.SeznamDodavatelu)
                    dodavatele.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
            }
            return dodavatele;
        }

        private void ZpracovatDefiniciDodavatele(int verze, PrehledObjednavekDataSeznamDodavatelu dodavatele, DefinovanDodavatelEvent message)
        {
            PrehledObjednavekDataDodavatele dodavatel;
            if (!dodavatele.IndexDodavatelu.TryGetValue(message.Kod, out dodavatel))
            {
                dodavatel = new PrehledObjednavekDataDodavatele();
                dodavatel.KodDodavatele = message.Kod;
                dodavatele.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
                dodavatele.SeznamDodavatelu.Add(dodavatel);
            }
            dodavatel.NazevDodavatele = message.Nazev;
            _cacheDodavatelu.Insert("dodavatele", verze, dodavatele, dirty: true);

            // TODO: aktualizovat objednavky
        }

        private IEnumerable<Task> UpravitObjednavkuInternal(string kodDodavatele, string cisloObjednavky, Guid eventId, DateTime? datum, DateTime? termin, Action<ObjednavkaVPrehledu> zmena)
        {
            var dokumentObjednavky = DokumentObjednavky(kodDodavatele, cisloObjednavky);

            var taskVerze = _cacheVerze.Get("verze", load => _repository.NacistVerziSeznamu());
            yield return taskVerze;
            var verzeDokumentuVerze = taskVerze.Result.Version;
            var verzeSeznamu = taskVerze.Result.Value;

            var taskDodavatele = _cacheDodavatelu.Get("dodavatele", NacistDodavatele);
            yield return taskDodavatele;
            PrehledObjednavekDataDodavatele dodavatel;
            taskDodavatele.Result.Value.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel);

            var taskObjednavka = _cacheObjednavek.Get(dokumentObjednavky, load => _repository.NacistObjednavku(kodDodavatele, cisloObjednavky));
            yield return taskObjednavka;
            var verzeObjednavky = taskObjednavka.Result.Version;
            var objednavka = taskObjednavka.Result.Value;

            if (objednavka == null)
            {
                objednavka = new PrehledObjednavekDataObjednavky();
                objednavka.KodDodavatele = kodDodavatele;
                objednavka.Objednavka = cisloObjednavky;
            }

            if (dodavatel != null)
                objednavka.NazevDodavatele = dodavatel.NazevDodavatele;

            if (datum.HasValue)
                objednavka.DatumObjednani = datum.Value;
            if (termin.HasValue)
                objednavka.TerminDodani = termin.Value;

            zmena(objednavka);

            _cacheObjednavek.Insert(dokumentObjednavky, verzeObjednavky, objednavka, dirty: true);
            _cacheVerze.Insert("verze", verzeDokumentuVerze, verzeSeznamu + 1, dirty: true);
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavkuInternal(
                message.KodDodavatele, message.Objednavka, message.EventId, message.Datum, message.TerminDodani, obj =>
                {
                    obj.PocetObjednanych += 1;
                })).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavkuInternal(
                message.KodDodavatele, message.Objednavka, message.EventId, message.Datum, null, obj =>
                {
                    if (message.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += 1;
                    else
                        obj.PocetOpravenych += 1;
                })).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavkuInternal(
                message.KodDodavatele, message.Objednavka, message.EventId, message.Datum, message.TerminDodani, obj =>
                {
                    obj.PocetObjednanych += message.Pocet;
                })).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavkuInternal(
                message.KodDodavatele, message.Objednavka, message.EventId, message.Datum, null, obj =>
                {
                    if (message.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += message.Pocet;
                    else
                        obj.PocetOpravenych += message.Pocet;
                })).GetTask();
        }

        public static string DokumentObjednavky(string kodDodavatele, string cisloObjednavky)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", kodDodavatele, "-", cisloObjednavky);
        }
    }

    public class PrehledObjednavekRepository
    {
        private IDocumentFolder _folder;

        public PrehledObjednavekRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<PrehledObjednavekDataObjednavky>> NacistObjednavku(string kodDodavatele, string cisloObjednavky)
        {
            return _folder.GetDocument(PrehledObjednavekProjection.DokumentObjednavky(kodDodavatele, cisloObjednavky))
                .ToMemoryCacheItem(DeserializovatObjednavku);
        }

        public Task<int> UlozitObjednavku(int verze, PrehledObjednavekDataObjednavky data)
        {
            return ProjectorUtils.Save(_folder, 
                PrehledObjednavekProjection.DokumentObjednavky(data.KodDodavatele, data.Objednavka),
                verze, JsonSerializer.SerializeToString(data), IndexyObjednavky(data));
        }

        private DocumentIndexing[] IndexyObjednavky(PrehledObjednavekDataObjednavky data)
        {
            return new[]
            {
                new DocumentIndexing("cisloObjednavky", string.Concat(data.Objednavka, "::", data.KodDodavatele)),
                new DocumentIndexing("datumObjednavky", data.DatumObjednani.ToString("s"))
            };
        }

        private PrehledObjednavekDataObjednavky DeserializovatObjednavku(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            else
                return JsonSerializer.DeserializeFromString<PrehledObjednavekDataObjednavky>(raw);
        }

        public Task<Tuple<int, List<PrehledObjednavekDataObjednavky>>> NacistSeznamObjednavekPodleCisla(int offset, int pocet)
        {
            return _folder.FindDocuments("cisloObjednavky", null, null, offset, pocet, true)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, task.Result.Select(o => DeserializovatObjednavku(o.Contents)).ToList()));
        }

        public Task<Tuple<int, List<PrehledObjednavekDataObjednavky>>> NacistSeznamObjednavekPodleData(int offset, int pocet)
        {
            return _folder.FindDocuments("datumObjednavky", null, null, offset, pocet, false)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, task.Result.Select(o => DeserializovatObjednavku(o.Contents)).ToList()));
        }

        public Task<MemoryCacheItem<PrehledObjednavekDataSeznamDodavatelu>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<PrehledObjednavekDataSeznamDodavatelu>);
        }

        public Task<int> UlozitDodavatele(int verze, PrehledObjednavekDataSeznamDodavatelu data)
        {
            return ProjectorUtils.Save(_folder, "dodavatele", verze, JsonSerializer.SerializeToString(data), null);
        }

        public Task<MemoryCacheItem<int>> NacistVerziSeznamu()
        {
            return _folder.GetDocument("verzeSeznamu").ToMemoryCacheItem(DeserializovatVerziSeznamu);
        }

        public Task<int> UlozitVerziSeznamu(int verzeDokumentu, int verzeSeznamu)
        {
            return ProjectorUtils.Save(_folder, "verzeSeznamu", verzeDokumentu, verzeSeznamu.ToString(), null);
        }

        private int DeserializovatVerziSeznamu(string raw)
        {
            int verzeSeznamu = 0;
            if (!string.IsNullOrEmpty(raw))
                int.TryParse(raw, out verzeSeznamu);
            return verzeSeznamu;
        }
    }

    public class PrehledObjednavekDataObjednavky : ObjednavkaVPrehledu
    {
    }

    public class PrehledObjednavekDataSeznamDodavatelu
    {
        public List<PrehledObjednavekDataDodavatele> SeznamDodavatelu { get; set; }
        [NonSerialized]
        public Dictionary<string, PrehledObjednavekDataDodavatele> IndexDodavatelu;
    }
    public class PrehledObjednavekDataDodavatele
    {
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
    }

    public class PrehledObjednavekReader
        : IAnswer<PrehledObjednavekRequest, PrehledObjednavekResponse>
    {
        private PrehledObjednavekRepository _repository;
        private MemoryCache<int> _cacheVerze;
        private MemoryCache<PrehledObjednavekResponse> _cachePodleCisla;
        private MemoryCache<PrehledObjednavekResponse> _cachePodleData;

        public PrehledObjednavekReader(PrehledObjednavekRepository repository, ITime time)
        {
            _repository = repository;
            _cacheVerze = new MemoryCache<int>(time);
            _cachePodleCisla = new MemoryCache<PrehledObjednavekResponse>(time);
            _cachePodleData = new MemoryCache<PrehledObjednavekResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<PrehledObjednavekRequest, PrehledObjednavekResponse>(this);
        }

        public Task<PrehledObjednavekResponse> Handle(PrehledObjednavekRequest message)
        {
            return TaskUtils.FromEnumerable<PrehledObjednavekResponse>(HandleInternal(message)).GetTask();
        }

        public IEnumerable<Task> HandleInternal(PrehledObjednavekRequest message)
        {
            var taskVerze = _cacheVerze.Get("verze", load => _repository.NacistVerziSeznamu());
            yield return taskVerze;
            var verzeSeznamu = taskVerze.Result.Value;

            if (message.Razeni == PrehledObjednavekRazeni.PodleCislaObjednavky)
            {
                var taskSeznam = _cachePodleCisla.Get(message.Stranka.ToString(), load =>
                {
                    if (verzeSeznamu == load.OldVersion)
                        return TaskUtils.FromResult<MemoryCacheItem<PrehledObjednavekResponse>>(null);
                    return _repository.NacistSeznamObjednavekPodleCisla(message.Stranka * 100 - 100, 100)
                        .ContinueWith(task => new MemoryCacheItem<PrehledObjednavekResponse>(verzeSeznamu, VytvoritResponse(message, task.Result.Item1, task.Result.Item2)));
                }).ExtractValue();
                yield return taskSeznam;
            }
            else
            {
                var taskSeznam = _cachePodleData.Get(message.Stranka.ToString(), load =>
                {
                    if (verzeSeznamu == load.OldVersion)
                        return TaskUtils.FromResult<MemoryCacheItem<PrehledObjednavekResponse>>(null);
                    return _repository.NacistSeznamObjednavekPodleData(message.Stranka * 100 - 100, 100)
                        .ContinueWith(task => new MemoryCacheItem<PrehledObjednavekResponse>(verzeSeznamu, VytvoritResponse(message, task.Result.Item1, task.Result.Item2)));
                }).ExtractValue();
                yield return taskSeznam;
            }
        }

        private static PrehledObjednavekResponse VytvoritResponse(PrehledObjednavekRequest request, int celkem, List<PrehledObjednavekDataObjednavky> seznam)
        {
            return new PrehledObjednavekResponse
            {
                Stranka = request.Stranka,
                Razeni = request.Razeni,
                PocetCelkem = celkem,
                PocetStranek = (celkem + 99) / 100,
                Seznam = seznam.Select(KonverzeNaOdpoved).ToList()
            };
        }

        private static ObjednavkaVPrehledu KonverzeNaOdpoved(PrehledObjednavekDataObjednavky orig)
        {
            return new ObjednavkaVPrehledu
            {
                DatumObjednani = orig.DatumObjednani,
                KodDodavatele = orig.KodDodavatele,
                NazevDodavatele = orig.NazevDodavatele,
                Objednavka = orig.Objednavka,
                TerminDodani = orig.TerminDodani,
                PocetNeopravitelnych = orig.PocetNeopravitelnych,
                PocetObjednanych = orig.PocetObjednanych,
                PocetOpravenych = orig.PocetOpravenych
            };
        }

    }
}
