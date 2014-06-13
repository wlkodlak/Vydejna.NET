using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Projections.IndexObjednavekReadModel
{
    public class IndexObjednavekProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<CislovaneNaradiPredanoKOpraveEvent>
        , IProcess<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcess<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<NecislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<DefinovanDodavatelEvent>
    {
        private IndexObjednavekRepository _repository;
        private MemoryCache<IndexObjednavekDodavatele> _cacheDodavatelu;
        private MemoryCache<IndexObjednavekDataObjednavek> _cacheObjednavek;
        private MemoryCache<IndexObjednavekDataDodacichListu> _cacheDodacichListu;

        public IndexObjednavekProjection(IndexObjednavekRepository repository, ITime time)
        {
            _repository = repository;
            _cacheDodavatelu = new MemoryCache<IndexObjednavekDodavatele>(time);
            _cacheObjednavek = new MemoryCache<IndexObjednavekDataObjednavek>(time);
            _cacheDodacichListu = new MemoryCache<IndexObjednavekDataDodacichListu>(time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<DefinovanDodavatelEvent>(this);
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return GetVersion() == storedVersion ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public Task Handle(ProjectorMessages.Reset message)
        {
            _cacheDodavatelu.Clear();
            _cacheObjednavek.Clear();
            _cacheDodacichListu.Clear();
            return _repository.Reset();
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

            var taskDodaciListy = _cacheDodacichListu.Flush(save => _repository.UlozitDodaciList(save.Version, save.Value));
            yield return taskDodaciListy;
            taskDodaciListy.Wait();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatObjednavku(message.Objednavka, message.KodDodavatele, message.TerminDodani)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatObjednavku(message.Objednavka, message.KodDodavatele, message.TerminDodani)).GetTask();
        }

        private IEnumerable<Task> ZpracovatObjednavku(string cisloObjednavky, string kodDodavatele, DateTime terminDodani)
        {
            var taskObjednavka = _cacheObjednavek.Get(cisloObjednavky, load => _repository.NacistObjednavku(cisloObjednavky, load.OldVersion));
            yield return taskObjednavka;
            var verzeObjednavky = taskObjednavka.Result.Version;
            var objednavka = taskObjednavka.Result.Value;

            var taskDodavatele = _cacheDodavatelu.Get("dodavatele", load => _repository.NacistDodavatele());
            yield return taskDodavatele;
            var dodavatele = taskDodavatele.Result.Value;

            if (objednavka == null)
            {
                objednavka = new IndexObjednavekDataObjednavek();
                objednavka.CisloObjednavky = cisloObjednavky;
                objednavka.Kandidati = new List<NalezenaObjednavka>();
            }
            var existujici = objednavka.Kandidati.FirstOrDefault(k => k.KodDodavatele == kodDodavatele);
            if (existujici == null)
            {
                existujici = new NalezenaObjednavka();
                existujici.Objednavka = cisloObjednavky;
                existujici.KodDodavatele = kodDodavatele;
                objednavka.Kandidati.Add(existujici);
            }
            IndexObjednavekDodavatel dodavatel;
            if (dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel))
                existujici.NazevDodavatele = dodavatel.Nazev;
            existujici.TerminDodani = terminDodani;

            _cacheObjednavek.Insert(cisloObjednavky, verzeObjednavky, objednavka, dirty: true);
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatDodaciList(message.DodaciList, message.KodDodavatele, message.Objednavka)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(ZpracovatDodaciList(message.DodaciList, message.KodDodavatele, message.Objednavka)).GetTask();
        }

        private IEnumerable<Task> ZpracovatDodaciList(string cisloDodacihoListu, string kodDodavatele, string cisloObjednavky)
        {
            var taskDodaciListy = _cacheDodacichListu.Get(cisloDodacihoListu, load => _repository.NacistDodaciList(cisloDodacihoListu, load.OldVersion));
            yield return taskDodaciListy;
            var dodaciListy = taskDodaciListy.Result.Value;
            var verzeDodListu = taskDodaciListy.Result.Version;

            var taskDodavatele = _cacheDodavatelu.Get("dodavatele", load => _repository.NacistDodavatele());
            yield return taskDodavatele;
            var dodavatele = taskDodavatele.Result.Value;

            if (dodaciListy == null)
            {
                dodaciListy = new IndexObjednavekDataDodacichListu();
                dodaciListy.CisloDodacihoListu = cisloDodacihoListu;
                dodaciListy.Kandidati = new List<NalezenyDodaciList>();
            }
            var existujici = dodaciListy.Kandidati.FirstOrDefault(k => k.KodDodavatele == kodDodavatele);
            if (existujici == null)
            {
                existujici = new NalezenyDodaciList();
                existujici.DodaciList = cisloDodacihoListu;
                existujici.KodDodavatele = kodDodavatele;
                dodaciListy.Kandidati.Add(existujici);
            }
            existujici.Objednavky = existujici.Objednavky ?? new List<string>();
            IndexObjednavekDodavatel dodavatel;
            if (dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel))
                existujici.NazevDodavatele = dodavatel.Nazev;
            if (!existujici.Objednavky.Contains(cisloObjednavky))
                existujici.Objednavky.Add(cisloObjednavky);

            _cacheDodacichListu.Insert(cisloDodacihoListu, verzeDodListu, dodaciListy, dirty: true);
        }

        private static IndexObjednavekDodavatele RozsiritData(IndexObjednavekDodavatele data)
        {
            if (data == null)
            {
                data = new IndexObjednavekDodavatele();
                data.Dodavatele = new List<IndexObjednavekDodavatel>();
                data.IndexDodavatelu = new Dictionary<string, IndexObjednavekDodavatel>();
            }
            else if (data.IndexDodavatelu == null)
            {
                data.IndexDodavatelu = new Dictionary<string, IndexObjednavekDodavatel>();
                foreach (var dodavatel in data.Dodavatele)
                    data.IndexDodavatelu[dodavatel.Kod] = dodavatel;
            }
            return data;
        }

        public Task Handle(DefinovanDodavatelEvent message)
        {
            return _cacheDodavatelu.Get("dodavatele", load => _repository.NacistDodavatele()
                .Transform(RozsiritData)).ContinueWith(task =>
            {
                var data = task.Result.Value;
               
                IndexObjednavekDodavatel existujici;
                if (!data.IndexDodavatelu.TryGetValue(message.Kod, out existujici))
                {
                    existujici = new IndexObjednavekDodavatel();
                    existujici.Kod = message.Kod;
                    data.Dodavatele.Add(existujici);
                    data.IndexDodavatelu[existujici.Kod] = existujici;
                }
                existujici.Nazev = message.Nazev;
             
                _cacheDodavatelu.Insert("dodavatele", task.Result.Version, data, dirty: true);
            });
        }
    }

    public class IndexObjednavekRepository
    {
        private IDocumentFolder _folder;

        public IndexObjednavekRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task<MemoryCacheItem<IndexObjednavekDodavatele>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<IndexObjednavekDodavatele>);
        }

        public Task<int> UlozitDodavatele(int verze, IndexObjednavekDodavatele data)
        {
            return ProjectorUtils.Save(_folder, "dodavatele", verze, JsonSerializer.SerializeToString(data), null);
        }

        public Task<MemoryCacheItem<IndexObjednavekDataObjednavek>> NacistObjednavku(string cisloObjednavky, int znamaVerze)
        {
            return _folder.GetNewerDocument(NazevDokumentuObjednavky(cisloObjednavky), znamaVerze).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<IndexObjednavekDataObjednavek>);
        }

        public Task<int> UlozitObjednavku(int verze, IndexObjednavekDataObjednavek data)
        {
            return ProjectorUtils.Save(_folder, NazevDokumentuObjednavky(data.CisloObjednavky), verze, JsonSerializer.SerializeToString(data), null);
        }

        public Task<MemoryCacheItem<IndexObjednavekDataDodacichListu>> NacistDodaciList(string cisloDodacihoListu, int znamaVerze)
        {
            return _folder.GetNewerDocument(NazevDokumentuDodacihoListu(cisloDodacihoListu), znamaVerze).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<IndexObjednavekDataDodacichListu>);
        }

        public Task<int> UlozitDodaciList(int verze, IndexObjednavekDataDodacichListu data)
        {
            return ProjectorUtils.Save(_folder, NazevDokumentuDodacihoListu(data.CisloDodacihoListu), verze, JsonSerializer.SerializeToString(data), null);
        }

        private static string NazevDokumentuObjednavky(string cislo)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", cislo);
        }

        private static string NazevDokumentuDodacihoListu(string cislo)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("dodacilist-", cislo);
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }
    }

    public class IndexObjednavekDataObjednavek
    {
        public string CisloObjednavky { get; set; }
        public List<NalezenaObjednavka> Kandidati { get; set; }
    }
    public class IndexObjednavekDataDodacichListu
    {
        public string CisloDodacihoListu { get; set; }
        public List<NalezenyDodaciList> Kandidati { get; set; }
    }
    public class IndexObjednavekDodavatele
    {
        public List<IndexObjednavekDodavatel> Dodavatele { get; set; }
        public Dictionary<string, IndexObjednavekDodavatel> IndexDodavatelu;
    }
    public class IndexObjednavekDodavatel
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
    }

    public class IndexObjednavekReader
        : IAnswer<NajitObjednavkuRequest, NajitObjednavkuResponse>
        , IAnswer<NajitDodaciListRequest, NajitDodaciListResponse>
    {
        private MemoryCache<NajitObjednavkuResponse> _cacheObjednavek;
        private MemoryCache<NajitDodaciListResponse> _cacheDodacichListu;
        private IndexObjednavekRepository _repository;

        public IndexObjednavekReader(IndexObjednavekRepository repository, ITime time)
        {
            _repository = repository;
            _cacheObjednavek = new MemoryCache<NajitObjednavkuResponse>(time);
            _cacheDodacichListu = new MemoryCache<NajitDodaciListResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<NajitObjednavkuRequest, NajitObjednavkuResponse>(this);
            bus.Subscribe<NajitDodaciListRequest, NajitDodaciListResponse>(this);
        }

        public Task<NajitObjednavkuResponse> Handle(NajitObjednavkuRequest message)
        {
            return _cacheObjednavek.Get(message.Objednavka,
                load => _repository.NacistObjednavku(message.Objednavka, load.OldVersion)
                .Transform(data => VytvoritResponseObjednavky(message.Objednavka, data)))
                .ExtractValue();
        }

        private NajitObjednavkuResponse VytvoritResponseObjednavky(string cisloObjednavky, IndexObjednavekDataObjednavek zaklad)
        {
            return new NajitObjednavkuResponse
            {
                Objednavka = cisloObjednavky,
                Nalezena = zaklad != null,
                Kandidati = zaklad == null ? new List<NalezenaObjednavka>() : zaklad.Kandidati
            };
        }

        public Task<NajitDodaciListResponse> Handle(NajitDodaciListRequest message)
        {
            return _cacheDodacichListu.Get(message.DodaciList,
                load => _repository.NacistDodaciList(message.DodaciList, load.OldVersion)
                .Transform(data => VytvoritResponseDodacihoListu(message.DodaciList, data)))
                .ExtractValue();
        }

        private NajitDodaciListResponse VytvoritResponseDodacihoListu(string dodaciList, IndexObjednavekDataDodacichListu zaklad)
        {
            return new NajitDodaciListResponse
            {
                DodaciList = dodaciList,
                Nalezen = zaklad != null,
                Kandidati = zaklad == null ? new List<NalezenyDodaciList>() : zaklad.Kandidati
            };
        }
    }
}
