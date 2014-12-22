using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.NaradiNaVydejneReadModel
{
    public class NaradiNaVydejneProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<ProjectorMessages.Flush>
        , IProcessEvent<DefinovanoNaradiEvent>
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
        private NaradiNaVydejneRepository _repository;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<NaradiNaVydejne> _cacheVydejna;

        public NaradiNaVydejneProjection(NaradiNaVydejneRepository repository, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
            _cacheVydejna = new MemoryCache<NaradiNaVydejne>(time);
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
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
            _cacheVydejna.Clear();
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
            var taskNaradi = _cacheNaradi.Flush(save => _repository.UlozitDefinici(save.Version, save.Value));
            yield return taskNaradi;
            taskNaradi.Wait();

            var taskVydejna = _cacheVydejna.Flush(save => _repository.UlozitUmistene(save.Version, save.Value, save.Value.PocetCelkem == 0));
            yield return taskVydejna;
            taskVydejna.Wait();
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            return _cacheNaradi.Get(DokumentDefiniceNaradi(message.NaradiId), load => _repository.NacistDefinici(message.NaradiId))
                .ContinueWith(task => ZpracovatDefiniciNaradi(message, task.Result.Version, task.Result.Value));
        }

        private void ZpracovatDefiniciNaradi(DefinovanoNaradiEvent message, int verze, InformaceONaradi naradi)
        {
            if (naradi == null)
            {
                naradi = new InformaceONaradi();
                naradi.NaradiId = message.NaradiId;
            }
            naradi.Vykres = message.Vykres;
            naradi.Rozmer = message.Rozmer;
            naradi.Druh = message.Druh;
            _cacheNaradi.Insert(DokumentDefiniceNaradi(message.NaradiId), verze, naradi, dirty: true);
        }

        public static string DokumentDefiniceNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public static string DokumentNaradiNaVydejne(Guid naradiId, StavNaradi stavNaradi)
        {
            return string.Concat("vydejna-", naradiId.ToString("N"), "-", stavNaradi.ToString().ToLowerInvariant());
        }

        private IEnumerable<Task> UpravitNaradi(Guid naradiId, int cisloNaradi, StavNaradi stavNaradi, int pocetCelkem)
        {
            var taskNaradi = _cacheNaradi.Get(DokumentDefiniceNaradi(naradiId), load => _repository.NacistDefinici(naradiId));
            yield return taskNaradi;
            var definice = taskNaradi.Result.Value;

            var taskVydejna = _cacheVydejna.Get(DokumentNaradiNaVydejne(naradiId, stavNaradi), load => _repository.NacistUmistene(naradiId, stavNaradi));
            yield return taskVydejna;
            var verze = taskVydejna.Result.Version;
            var umistene = taskVydejna.Result.Value;

            if (definice == null)
            {
                definice = new InformaceONaradi();
                definice.NaradiId = naradiId;
                definice.Vykres = definice.Rozmer = definice.Druh = "";
            }

            if (umistene != null)
            {
                umistene.Vykres = definice.Vykres;
                umistene.Rozmer = definice.Rozmer;
                umistene.Druh = definice.Druh;
                if (cisloNaradi == 0)
                {
                    umistene.PocetNecislovanych = pocetCelkem;
                }
                else
                {
                    if (pocetCelkem == 0)
                        umistene.SeznamCislovanych.Remove(cisloNaradi);
                    else if (!umistene.SeznamCislovanych.Contains(cisloNaradi))
                        umistene.SeznamCislovanych.Add(cisloNaradi);
                }
                umistene.PocetCelkem = umistene.PocetCelkem + umistene.SeznamCislovanych.Count;

                _cacheVydejna.Insert(DokumentNaradiNaVydejne(naradiId, stavNaradi), verze, umistene, dirty: true);
            }
            else if (pocetCelkem > 0)
            {
                umistene = new NaradiNaVydejne();
                umistene.NaradiId = naradiId;
                umistene.StavNaradi = stavNaradi;
                umistene.Vykres = definice.Vykres;
                umistene.Rozmer = definice.Rozmer;
                umistene.Druh = definice.Druh;
                umistene.SeznamCislovanych = new List<int>();
                if (cisloNaradi == 0)
                    umistene.PocetNecislovanych = pocetCelkem;
                else
                    umistene.SeznamCislovanych.Add(cisloNaradi);
                umistene.PocetCelkem = pocetCelkem;
                _cacheVydejna.Insert(DokumentNaradiNaVydejne(naradiId, stavNaradi), verze, umistene, dirty: true);
            }
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, StavNaradi.VPoradku, 1)).GetTask();
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, StavNaradi.VPoradku, 0)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, message.StavNaradi, 1)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, StavNaradi.NutnoOpravit, 0)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, message.StavNaradi, 1)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, message.CisloNaradi, StavNaradi.Neopravitelne, 0)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, StavNaradi.VPoradku, message.PocetNaNovem)).GetTask();
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, StavNaradi.VPoradku, message.PocetNaPredchozim)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, message.StavNaradi, message.PocetNaNovem)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, StavNaradi.NutnoOpravit, message.PocetNaPredchozim)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, message.StavNaradi, message.PocetNaNovem)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            return TaskUtils.FromEnumerable(UpravitNaradi(message.NaradiId, 0, StavNaradi.Neopravitelne, message.PocetNaPredchozim)).GetTask();
        }
    }

    public class NaradiNaVydejneRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaVydejneRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<NaradiNaVydejne>> NacistUmistene(Guid naradiId, StavNaradi stavNaradi)
        {
            return _folder.GetDocument(NaradiNaVydejneProjection.DokumentNaradiNaVydejne(naradiId, stavNaradi)).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<NaradiNaVydejne>);
        }

        public Task<int> UlozitUmistene(int verze, NaradiNaVydejne data, bool smazat)
        {
            return EventProjectorUtils.Save(_folder, NaradiNaVydejneProjection.DokumentNaradiNaVydejne(data.NaradiId, data.StavNaradi), verze,
                smazat ? null : JsonSerializer.SerializeToString(data),
                smazat ? null : new[] { 
                    new DocumentIndexing("naradiId", data.NaradiId.ToString()),
                    new DocumentIndexing("podleVykresu", string.Concat(data.Vykres, data.Rozmer))
                });
        }

        public Task<MemoryCacheItem<InformaceONaradi>> NacistDefinici(Guid naradiId)
        {
            return _folder.GetDocument(NaradiNaVydejneProjection.DokumentDefiniceNaradi(naradiId)).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceONaradi>);
        }

        public Task<int> UlozitDefinici(int verze, InformaceONaradi data)
        {
            return EventProjectorUtils.Save(_folder, NaradiNaVydejneProjection.DokumentDefiniceNaradi(data.NaradiId), verze,
                JsonSerializer.SerializeToString(data), null);
        }

        public Task<Tuple<int, List<NaradiNaVydejne>>> NacistSeznamUmistenych(int offset, int pocet)
        {
            return _folder.FindDocuments("podleVykresu", null, null, offset, pocet, true)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, VytvoritSeznamUmistenych(task.Result)));
        }

        private static List<NaradiNaVydejne> VytvoritSeznamUmistenych(DocumentStoreFoundDocuments list)
        {
            return list
                .Where(doc => !string.IsNullOrEmpty(doc.Contents))
                .Select(doc => JsonSerializer.DeserializeFromString<NaradiNaVydejne>(doc.Contents))
                .ToList();
        }
    }

    public class NaradiNaVydejneReader
        : IAnswer<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>
    {
        private NaradiNaVydejneRepository _repository;
        private MemoryCache<ZiskatNaradiNaVydejneResponse> _cache;

        public NaradiNaVydejneReader(NaradiNaVydejneRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatNaradiNaVydejneResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>(this);
        }

        public Task<ZiskatNaradiNaVydejneResponse> Handle(ZiskatNaradiNaVydejneRequest message)
        {
            return _cache.Get(message.Stranka.ToString(), load => _repository.NacistSeznamUmistenych(message.Stranka * 100 - 100, 100)
                .ContinueWith(task => MemoryCacheItem.Create(1, VytvoritResponse(message, task.Result.Item1, task.Result.Item2))))
                .ExtractValue();
        }

        private ZiskatNaradiNaVydejneResponse VytvoritResponse(ZiskatNaradiNaVydejneRequest request, int pocetCelkem, IList<NaradiNaVydejne> list)
        {
            var response = new ZiskatNaradiNaVydejneResponse();
            response.Stranka = request.Stranka;
            response.PocetCelkem = pocetCelkem;
            response.PocetStranek = (response.PocetCelkem + 99) / 100;
            response.Seznam = list.ToList();
            return response;
        }
    }
}
