using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.NaradiNaPracovistiReadModel
{
    public class NaradiNaPracovistiProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<DefinovanoPracovisteEvent>
        , IProcessEvent<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<DefinovanoNaradiEvent>
    {
        private NaradiNaPracovistiRepository _repository;
        private MemoryCache<NaradiNaPracovistiDataPracoviste> _cachePracovist;
        private MemoryCache<InformaceONaradi> _cacheNaradi;

        public NaradiNaPracovistiProjection(NaradiNaPracovistiRepository repository, ITime time)
        {
            _repository = repository;
            _cachePracovist = new MemoryCache<NaradiNaPracovistiDataPracoviste>(time);
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoPracovisteEvent>(this);
            mgr.Register<CislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
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
            _cacheNaradi.Clear();
            _cachePracovist.Clear();
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
            var taskNaradi = _cacheNaradi.Flush(save => _repository.UlozitNaradi(save.Version, save.Value));
            yield return taskNaradi;
            taskNaradi.Wait();

            var taskPracoviste = _cachePracovist.Flush(save => _repository.UlozitPracoviste(save.Version, save.Value));
            yield return taskPracoviste;
            taskPracoviste.Wait();
        }

        private class NaradiIdComparer : IComparer<NaradiNaPracovisti>
        {
            public int Compare(NaradiNaPracovisti x, NaradiNaPracovisti y)
            {
                return x.NaradiId.CompareTo(y.NaradiId);
            }
        }

        private static NaradiNaPracovistiDataPracoviste RozsiritData(string kodPracoviste, NaradiNaPracovistiDataPracoviste data)
        {
            if (data == null)
            {
                data = new NaradiNaPracovistiDataPracoviste();
                data.Seznam = new List<NaradiNaPracovisti>();
                data.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaPracovisti>();
            }
            else if (data.IndexPodleIdNaradi == null)
            {
                data.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaPracovisti>();
                foreach (var naradi in data.Seznam)
                    data.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
            }
            if (data.Pracoviste == null)
            {
                data.Pracoviste = new InformaceOPracovisti();
                data.Pracoviste.Kod = kodPracoviste;
                data.Pracoviste.Nazev = "";
                data.Pracoviste.Stredisko = "";
            }
            return data;
        }

        public Task Handle(DefinovanoPracovisteEvent message)
        {
            return _cachePracovist.Get(message.Kod,
                load => _repository.NacistPracoviste(message.Kod, load.OldVersion).Transform(data => RozsiritData(message.Kod, data))
                ).ContinueWith(task => ZpracovatDefiniciPracoviste(message, task.Result.Version, task.Result.Value));
        }

        private void ZpracovatDefiniciPracoviste(DefinovanoPracovisteEvent message, int verze, NaradiNaPracovistiDataPracoviste data)
        {
            data.Pracoviste = new InformaceOPracovisti
            {
                Kod = message.Kod,
                Nazev = message.Nazev,
                Stredisko = message.Stredisko,
                Aktivni = !message.Deaktivovano
            };

            _cachePracovist.Insert(message.Kod, verze, data, dirty: true);
        }


        public Task Handle(DefinovanoNaradiEvent message)
        {
            return _cacheNaradi.Get(message.NaradiId.ToString(), load => _repository.NacistNaradi(message.NaradiId))
                .ContinueWith(task => ZpracovatDefiniciNaradi(message, task.Result.Version, task.Result.Value));
        }

        private void ZpracovatDefiniciNaradi(DefinovanoNaradiEvent message, int verze, InformaceONaradi naradiInfo)
        {
            naradiInfo = new InformaceONaradi();
            naradiInfo.NaradiId = message.NaradiId;
            naradiInfo.Vykres = message.Vykres;
            naradiInfo.Rozmer = message.Rozmer;
            naradiInfo.Druh = message.Druh;
            _cacheNaradi.Insert(naradiInfo.NaradiId.ToString(), verze, naradiInfo, dirty: true);
        }

        private IEnumerable<Task> UpravitNaradiNaPracovisti(string kodPracoviste, Guid naradiId, int cisloNaradi, int novyPocet, DateTime? datumVydeje)
        {
            var taskPracoviste = _cachePracovist.Get(kodPracoviste, load => _repository.NacistPracoviste(kodPracoviste, load.OldVersion)
                .Transform(data => RozsiritData(kodPracoviste, data)));
            yield return taskPracoviste;
            var verzePracoviste = taskPracoviste.Result.Version;
            var dataPracoviste = taskPracoviste.Result.Value;

            var taskNaradi = _cacheNaradi.Get(naradiId.ToString(), load => _repository.NacistNaradi(naradiId));
            yield return taskNaradi;
            var naradiInfo = taskNaradi.Result.Value;


                NaradiNaPracovisti naradiNaPracovisti;
                if (!dataPracoviste.IndexPodleIdNaradi.TryGetValue(naradiId, out naradiNaPracovisti))
                {
                    dataPracoviste.IndexPodleIdNaradi[naradiId] = naradiNaPracovisti = new NaradiNaPracovisti();
                    naradiNaPracovisti.NaradiId = naradiId;
                    naradiNaPracovisti.SeznamCislovanych = new List<int>();
                    var naradiComparer = new NaradiIdComparer();
                    var index = dataPracoviste.Seznam.BinarySearch(naradiNaPracovisti, naradiComparer);
                    if (index < 0)
                        index = ~index;
                    dataPracoviste.Seznam.Insert(index, naradiNaPracovisti);
                }
                if (naradiInfo != null)
                {
                    naradiNaPracovisti.Vykres = naradiInfo.Vykres;
                    naradiNaPracovisti.Rozmer = naradiInfo.Rozmer;
                    naradiNaPracovisti.Druh = naradiInfo.Druh;
                }

                if (datumVydeje.HasValue)
                    naradiNaPracovisti.DatumPoslednihoVydeje = datumVydeje.Value;
                if (cisloNaradi == 0)
                {
                    naradiNaPracovisti.PocetNecislovanych = novyPocet;
                }
                else
                {
                    if (novyPocet == 1 && !naradiNaPracovisti.SeznamCislovanych.Contains(cisloNaradi))
                        naradiNaPracovisti.SeznamCislovanych.Add(cisloNaradi);
                    else if (novyPocet == 0)
                        naradiNaPracovisti.SeznamCislovanych.Remove(cisloNaradi);
                }
                naradiNaPracovisti.PocetCelkem = naradiNaPracovisti.PocetNecislovanych + naradiNaPracovisti.SeznamCislovanych.Count;
                if (naradiNaPracovisti.PocetCelkem == 0)
                {
                    dataPracoviste.Seznam.Remove(naradiNaPracovisti);
                    dataPracoviste.IndexPodleIdNaradi.Remove(naradiNaPracovisti.NaradiId);
                }
                dataPracoviste.PocetCelkem = dataPracoviste.IndexPodleIdNaradi.Values.Sum(n => n.PocetCelkem);

            _cachePracovist.Insert(kodPracoviste, verzePracoviste, dataPracoviste, dirty: true);
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradiNaPracovisti(message.KodPracoviste, message.NaradiId, message.CisloNaradi, 1, message.Datum)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradiNaPracovisti(message.KodPracoviste, message.NaradiId, message.CisloNaradi, 0, null)).GetTask();
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradiNaPracovisti(message.KodPracoviste, message.NaradiId, 0, message.PocetNaNovem, message.Datum)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradiNaPracovisti(message.KodPracoviste, message.NaradiId, 0, message.PocetNaPredchozim, null)).GetTask();
        }
    }

    public class NaradiNaPracovistiDataPracoviste
    {
        public InformaceOPracovisti Pracoviste { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaPracovisti> Seznam { get; set; }
        [NonSerialized]
        public Dictionary<Guid, NaradiNaPracovisti> IndexPodleIdNaradi;
    }

    public class NaradiNaPracovistiRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaPracovistiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task<MemoryCacheItem<NaradiNaPracovistiDataPracoviste>> NacistPracoviste(string kodPracoviste, int znamaVerze)
        {
            return _folder.GetNewerDocument(DokumentPracoviste(kodPracoviste), znamaVerze).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<NaradiNaPracovistiDataPracoviste>);
        }

        private static string DokumentPracoviste(string kodPracoviste)
        {
            return "pracoviste-" + kodPracoviste;
        }

        public Task<int> UlozitPracoviste(int verze, NaradiNaPracovistiDataPracoviste data)
        {
            return EventProjectorUtils.Save(_folder, "pracoviste-" + data.Pracoviste.Kod, verze, JsonSerializer.SerializeToString(data), null);
        }

        public Task<MemoryCacheItem<InformaceONaradi>> NacistNaradi(Guid naradiId)
        {
            return _folder.GetDocument(DokumentNaradi(naradiId)).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceONaradi>);
        }

        public Task<int> UlozitNaradi(int verze, InformaceONaradi data)
        {
            return EventProjectorUtils.Save(_folder, DokumentNaradi(data.NaradiId), verze, JsonSerializer.SerializeToString(data), null);
        }

        private static string DokumentNaradi(Guid naradiId)
        {
            return "naradi-" + naradiId.ToString("N");
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }
    }

    public class NaradiNaPracovistiReader
        : IAnswer<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>
    {
        private NaradiNaPracovistiRepository _repository;
        private MemoryCache<ZiskatNaradiNaPracovistiResponse> _cache;

        public NaradiNaPracovistiReader(NaradiNaPracovistiRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatNaradiNaPracovistiResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>(this);
        }

        public Task<ZiskatNaradiNaPracovistiResponse> Handle(ZiskatNaradiNaPracovistiRequest message)
        {
            return _cache.Get(message.KodPracoviste,
                load => _repository.NacistPracoviste(message.KodPracoviste, load.OldVersion)
                    .Transform(zaklad => VytvoritResponse(message.KodPracoviste, zaklad))
                    ).ExtractValue();
        }

        private ZiskatNaradiNaPracovistiResponse VytvoritResponse(string kodPracoviste, NaradiNaPracovistiDataPracoviste zaklad)
        {
            if (zaklad == null)
            {
                return new ZiskatNaradiNaPracovistiResponse
                {
                    Pracoviste = new InformaceOPracovisti
                    {
                        Kod = kodPracoviste,
                        Aktivni = false,
                        Nazev = "",
                        Stredisko = ""
                    },
                    PracovisteExistuje = false,
                    PocetCelkem = 0,
                    Seznam = new List<NaradiNaPracovisti>()
                };
            }
            else
            {
                return new ZiskatNaradiNaPracovistiResponse
                {
                    Pracoviste = zaklad.Pracoviste,
                    PracovisteExistuje = true,
                    Seznam = zaklad.Seznam,
                    PocetCelkem = zaklad.PocetCelkem
                };
            }
        }
    }
}
