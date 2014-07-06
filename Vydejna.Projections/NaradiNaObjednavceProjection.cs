using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.NaradiNaObjednavceReadModel
{
    public class NaradiNaObjednavceProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<DefinovanDodavatelEvent>
        , IProcessEvent<DefinovanoNaradiEvent>
        , IProcessEvent<CislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZOpravyEvent>
    {
        private NaradiNaObjednavceRepository _repository;
        private MemoryCache<NaradiNaObjednavceDataObjednavky> _cacheObjednavek;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<NaradiNaObjednavceDataDodavatele> _cacheDodavatele;

        public NaradiNaObjednavceProjection(NaradiNaObjednavceRepository repository, ITime time)
        {
            _repository = repository;
            _cacheObjednavek = new MemoryCache<NaradiNaObjednavceDataObjednavky>(time);
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
            _cacheDodavatele = new MemoryCache<NaradiNaObjednavceDataDodavatele>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanDodavatelEvent>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
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
            _cacheDodavatele.Clear();
            _cacheNaradi.Clear();
            _cacheObjednavek.Clear();
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
            var taskDodavatele = _cacheDodavatele.Flush(save => _repository.UlozitDodavatele(save.Version, save.Value));
            yield return taskDodavatele;
            taskDodavatele.Wait();

            var taskNaradi = _cacheNaradi.Flush(save => _repository.UlozitNaradi(save.Version, save.Value));
            yield return taskNaradi;
            taskNaradi.Wait();

            var taskObjednavky = _cacheObjednavek.Flush(save => _repository.UlozitObjednavku(save.Version, save.Value));
            yield return taskObjednavky;
            taskObjednavky.Wait();
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            var klic = NaradiNaObjednavceRepository.DokumentNaradi(message.NaradiId);
            return _cacheNaradi.Get(klic, load => _repository.NacistNaradi(message.NaradiId))
                .ContinueWith(task => ZpracovatDefiniciNaradi(message, klic, task.Result.Version, task.Result.Value));
        }

        private void ZpracovatDefiniciNaradi(DefinovanoNaradiEvent message, string klic, int verze, InformaceONaradi naradiInfo)
        {
            if (naradiInfo == null)
            {
                naradiInfo = new InformaceONaradi();
                naradiInfo.NaradiId = message.NaradiId;
            }
            naradiInfo.Vykres = message.Vykres;
            naradiInfo.Rozmer = message.Rozmer;
            naradiInfo.Druh = message.Druh;
            _cacheNaradi.Insert(klic, verze, naradiInfo, dirty: true);
        }

        public Task Handle(DefinovanDodavatelEvent message)
        {
            return _cacheDodavatele.Get("dodavatele", load => _repository.NacistDodavatele().Transform(RozsiritData))
                .ContinueWith(task => ZpracovatDefiniciDodavatele(message, task.Result.Version, task.Result.Value));
        }

        private static NaradiNaObjednavceDataDodavatele RozsiritData(NaradiNaObjednavceDataDodavatele data)
        {
            if (data == null)
            {
                data = new NaradiNaObjednavceDataDodavatele();
                data.SeznamDodavatelu = new List<InformaceODodavateli>();
                data.IndexDodavatelu = new Dictionary<string, InformaceODodavateli>();
            }
            else if (data.IndexDodavatelu == null)
            {
                data.IndexDodavatelu = new Dictionary<string, InformaceODodavateli>();
                foreach (var dodavatel in data.SeznamDodavatelu)
                    data.IndexDodavatelu[dodavatel.Kod] = dodavatel;
            }
            return data;
        }

        private void ZpracovatDefiniciDodavatele(DefinovanDodavatelEvent message, int verze, NaradiNaObjednavceDataDodavatele dodavatele)
        {
            InformaceODodavateli dodavatelInfo;
            if (!dodavatele.IndexDodavatelu.TryGetValue(message.Kod, out dodavatelInfo))
            {
                dodavatelInfo = new InformaceODodavateli();
                dodavatelInfo.Kod = message.Kod;
                dodavatele.SeznamDodavatelu.Add(dodavatelInfo);
                dodavatele.IndexDodavatelu[dodavatelInfo.Kod] = dodavatelInfo;
            }
            dodavatelInfo.Nazev = message.Nazev;
            dodavatelInfo.Adresa = message.Adresa;
            dodavatelInfo.Ico = message.Ico;
            dodavatelInfo.Dic = message.Dic;
            dodavatelInfo.Aktivni = !message.Deaktivovan;
            _cacheDodavatele.Insert("dodavatele", verze, dodavatele, dirty: true);
        }

        private IEnumerable<Task> UpravitObjednavku(string kodDodavatele, string cisloObjednavky, Guid naradiId, int cisloNaradi, int novyPocet, DateTime? terminDodani)
        {
            var nazevDokumentuObjednavky = NaradiNaObjednavceRepository.DokumentObjednavky(kodDodavatele, cisloObjednavky);
            var taskObjednavky = _cacheObjednavek.Get(nazevDokumentuObjednavky, load => _repository.NacistObjednavku(kodDodavatele, cisloObjednavky));
            yield return taskObjednavky;
            var verzeObjednavky = taskObjednavky.Result.Version;
            var dataObjednavky = taskObjednavky.Result.Value;

            var taskNaradi = _cacheNaradi.Get(NaradiNaObjednavceRepository.DokumentNaradi(naradiId), load => _repository.NacistNaradi(naradiId));
            yield return taskNaradi;
            var naradiInfo = taskNaradi.Result.Value;

            var taskDodavatele = _cacheDodavatele.Get("dodavatele", load => _repository.NacistDodavatele().Transform(RozsiritData));
            yield return taskDodavatele;
            var dodavatele = taskDodavatele.Result.Value;
            InformaceODodavateli dodavatel;
            if (!dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel))
            {
                dodavatel = new InformaceODodavateli();
                dodavatel.Kod = kodDodavatele;
            }

            NaradiNaObjednavce naradiNaObjednavce;

            if (dataObjednavky == null)
            {
                dataObjednavky = new NaradiNaObjednavceDataObjednavky();
                dataObjednavky.Seznam = new List<NaradiNaObjednavce>();
                dataObjednavky.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaObjednavce>();
            }
            else if (dataObjednavky.IndexPodleIdNaradi == null)
            {
                dataObjednavky.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaObjednavce>();
                foreach (var naradi in dataObjednavky.Seznam)
                    dataObjednavky.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
            }

            dataObjednavky.Objednavka = cisloObjednavky;
            dataObjednavky.Dodavatel = dodavatel;
            if (terminDodani != null)
                dataObjednavky.TerminDodani = terminDodani;

            if (!dataObjednavky.IndexPodleIdNaradi.TryGetValue(naradiId, out naradiNaObjednavce))
            {
                naradiNaObjednavce = new NaradiNaObjednavce();
                naradiNaObjednavce.NaradiId = naradiId;
                naradiNaObjednavce.SeznamCislovanych = new List<int>();
                dataObjednavky.IndexPodleIdNaradi[naradiId] = naradiNaObjednavce;
                dataObjednavky.Seznam.Add(naradiNaObjednavce);
            }
            if (naradiInfo != null)
            {
                naradiNaObjednavce.Vykres = naradiInfo.Vykres;
                naradiNaObjednavce.Rozmer = naradiInfo.Rozmer;
                naradiNaObjednavce.Druh = naradiInfo.Druh;
            }

            if (cisloNaradi == 0)
            {
                naradiNaObjednavce.PocetNecislovanych = novyPocet;
            }
            else
            {
                if (novyPocet == 1 && !naradiNaObjednavce.SeznamCislovanych.Contains(cisloNaradi))
                    naradiNaObjednavce.SeznamCislovanych.Add(cisloNaradi);
                else if (novyPocet == 0)
                    naradiNaObjednavce.SeznamCislovanych.Remove(cisloNaradi);
            }
            naradiNaObjednavce.PocetCelkem = naradiNaObjednavce.PocetNecislovanych + naradiNaObjednavce.SeznamCislovanych.Count;
            dataObjednavky.PocetCelkem = dataObjednavky.IndexPodleIdNaradi.Values.Sum(n => n.PocetCelkem);

            if (naradiNaObjednavce.PocetCelkem == 0)
            {
                dataObjednavky.Seznam.Remove(naradiNaObjednavce);
                dataObjednavky.IndexPodleIdNaradi.Remove(naradiId);
            }
            
            _cacheObjednavek.Insert(nazevDokumentuObjednavky, verzeObjednavky, dataObjednavky, dirty: true);
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavku(message.KodDodavatele, message.Objednavka,
                message.NaradiId, message.CisloNaradi, 1, message.TerminDodani)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavku(message.KodDodavatele, message.Objednavka,
                message.NaradiId, message.CisloNaradi, 0, null)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavku(message.KodDodavatele, message.Objednavka,
                message.NaradiId, 0, message.PocetNaNovem, message.TerminDodani)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitObjednavku(message.KodDodavatele, message.Objednavka,
                message.NaradiId, 0, message.PocetNaPredchozim, null)).GetTask();
        }
    }

    public class NaradiNaObjednavceDataObjednavky
    {
        public InformaceODodavateli Dodavatel { get; set; }
        public string Objednavka { get; set; }
        public DateTime? TerminDodani { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaObjednavce> Seznam { get; set; }
        [NonSerialized]
        public Dictionary<Guid, NaradiNaObjednavce> IndexPodleIdNaradi;
    }
    public class NaradiNaObjednavceDataDodavatele
    {
        public List<InformaceODodavateli> SeznamDodavatelu { get; set; }
        [NonSerialized]
        public Dictionary<string, InformaceODodavateli> IndexDodavatelu;
    }

    public class NaradiNaObjednavceRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaObjednavceRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task<MemoryCacheItem<NaradiNaObjednavceDataDodavatele>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataDodavatele>);
        }

        public Task<int> UlozitDodavatele(int verze, NaradiNaObjednavceDataDodavatele dodavatele)
        {
            return ProjectorUtils.Save(_folder, "dodavatele", verze, JsonSerializer.SerializeToString(dodavatele), null);
        }

        public static string DokumentObjednavky(string kodDodavatele, string cisloObjednavky)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", kodDodavatele, "-", cisloObjednavky);
        }

        public Task<MemoryCacheItem<NaradiNaObjednavceDataObjednavky>> NacistObjednavku(string kodDodavatele, string cisloObjednavky)
        {
            return _folder.GetDocument(DokumentObjednavky(kodDodavatele, cisloObjednavky)).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataObjednavky>);
        }

        public Task<int> UlozitObjednavku(int verze, NaradiNaObjednavceDataObjednavky objednavka)
        {
            return ProjectorUtils.Save(_folder, DokumentObjednavky(objednavka.Dodavatel.Kod, objednavka.Objednavka), verze, JsonSerializer.SerializeToString(objednavka), null);
        }

        public static string DokumentNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public Task<MemoryCacheItem<InformaceONaradi>> NacistNaradi(Guid naradiId)
        {
            return _folder.GetDocument(DokumentNaradi(naradiId)).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceONaradi>);
        }

        public Task<int> UlozitNaradi(int verze, InformaceONaradi naradi)
        {
            return ProjectorUtils.Save(_folder, DokumentNaradi(naradi.NaradiId), verze, JsonSerializer.SerializeToString(naradi), null);
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }
    }

    public class NaradiNaObjednavceReader
       : IAnswer<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>
    {
        private MemoryCache<ZiskatNaradiNaObjednavceResponse> _cache;
        private NaradiNaObjednavceRepository _repository;

        public NaradiNaObjednavceReader(NaradiNaObjednavceRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatNaradiNaObjednavceResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>(this);
        }

        public Task<ZiskatNaradiNaObjednavceResponse> Handle(ZiskatNaradiNaObjednavceRequest message)
        {
            return _cache.Get(
                NaradiNaObjednavceRepository.DokumentObjednavky(message.KodDodavatele, message.Objednavka),
                load => _repository.NacistObjednavku(message.KodDodavatele, message.Objednavka)
                    .Transform(data => VytvoritResponse(message.KodDodavatele, message.Objednavka, data)))
                .ExtractValue();
        }

        private ZiskatNaradiNaObjednavceResponse VytvoritResponse(string kodDodavatele, string cisloObjednavky, NaradiNaObjednavceDataObjednavky zaklad)
        {
            if (zaklad == null)
            {
                return new ZiskatNaradiNaObjednavceResponse
                {
                    ObjednavkaExistuje = false,
                    Dodavatel = new InformaceODodavateli
                    {
                        Kod = kodDodavatele,
                        Aktivni = false,
                        Nazev = "",
                        Adresa = new string[0],
                        Ico = "",
                        Dic = ""
                    },
                    TerminDodani = null,
                    Objednavka = cisloObjednavky,
                    PocetCelkem = 0,
                    Seznam = new List<NaradiNaObjednavce>()
                };
            }
            else
            {
                return new ZiskatNaradiNaObjednavceResponse
                {
                    ObjednavkaExistuje = true,
                    Objednavka = zaklad.Objednavka,
                    TerminDodani = zaklad.TerminDodani,
                    Dodavatel = zaklad.Dodavatel,
                    Seznam = zaklad.Seznam,
                    PocetCelkem = zaklad.PocetCelkem
                };
            }
        }
    }
}
