using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using Vydejna.Contracts;

namespace Vydejna.Projections.PrehledCislovanehoNaradiReadModel
{
    public class PrehledCislovanehoNaradiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanoNaradiEvent>
        , IProcess<CislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcess<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcess<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcess<CislovaneNaradiPredanoKOpraveEvent>
        , IProcess<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<CislovaneNaradiPredanoKeSesrotovaniEvent>
    {
        private PrehledCislovanehoNaradiRepository _repository;
        private MemoryCache<CislovaneNaradiVPrehledu> _cacheCislovane;
        private MemoryCache<InformaceONaradi> _cacheNaradi;

        public PrehledCislovanehoNaradiProjection(PrehledCislovanehoNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheCislovane = new MemoryCache<CislovaneNaradiVPrehledu>(time);
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoNaVydejnuEvent>(this);
            mgr.Register<CislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKeSesrotovaniEvent>(this);
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
            _cacheNaradi.Clear();
            _cacheCislovane.Clear();
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

            var taskCislovane = _cacheCislovane.Flush(save => _repository.UlozitCislovane(save.Version, save.Value, ZobrazitVPrehledu(save.Value)));
            yield return taskCislovane;
            taskCislovane.Wait();
        }

        private static bool ZobrazitVPrehledu(CislovaneNaradiVPrehledu data)
        {
            return data.Umisteni != null && data.Umisteni.ZakladniUmisteni != ZakladUmisteni.VeSrotu;
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            return _cacheNaradi.Get(DokumentNaradi(message.NaradiId), load => _repository.NacistNaradi(message.NaradiId)).ContinueWith(task =>
            {
                var naradi = task.Result.Value;
                if (naradi == null)
                {
                    naradi = new InformaceONaradi();
                    naradi.NaradiId = message.NaradiId;
                }
                naradi.Vykres = message.Vykres;
                naradi.Rozmer = message.Rozmer;
                naradi.Druh = message.Druh;

                _cacheNaradi.Insert(DokumentNaradi(message.NaradiId), task.Result.Version, naradi, dirty: true);
            });
        }

        private IEnumerable<Task> PresunNaradi(Guid naradiId, int cisloNaradi, UmisteniNaradiDto umisteni, decimal cena)
        {
            var taskNaradi = _cacheNaradi.Get(DokumentNaradi(naradiId), load => _repository.NacistNaradi(naradiId));
            yield return taskNaradi;
            var naradiInfo = taskNaradi.Result.Value;

            var dokumentCislovane = DokumentCislovane(naradiId, cisloNaradi);
            var taskCislovane = _cacheCislovane.Get(dokumentCislovane, load => _repository.NacistCislovane(naradiId, cisloNaradi));
            yield return taskCislovane;
            var verzeCislovane = taskCislovane.Result.Version;
            var dataCislovane = taskCislovane.Result.Value;

            if (dataCislovane == null)
            {
                dataCislovane = new CislovaneNaradiVPrehledu();
                dataCislovane.NaradiId = naradiId;
                dataCislovane.CisloNaradi = cisloNaradi;
            }
            if (naradiInfo != null)
            {
                dataCislovane.Vykres = naradiInfo.Vykres;
                dataCislovane.Rozmer = naradiInfo.Rozmer;
                dataCislovane.Druh = naradiInfo.Druh;
            }
            dataCislovane.Umisteni = umisteni;
            dataCislovane.Cena = cena;

            _cacheCislovane.Insert(dokumentCislovane, verzeCislovane, dataCislovane, dirty: true);
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            return TaskUtils.FromEnumerable(PresunNaradi(message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova)).GetTask();
        }

        public static string DokumentNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public static string DokumentCislovane(Guid naradiId, int cisloNaradi)
        {
            return string.Concat("cislovane-", naradiId.ToString("N"), "-", cisloNaradi.ToString());
        }
    }

    public class PrehledCislovanehoNaradiRepository
    {
        private IDocumentFolder _folder;

        public PrehledCislovanehoNaradiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<InformaceONaradi>> NacistNaradi(Guid naradiId)
        {
            return _folder.GetDocument(PrehledCislovanehoNaradiProjection.DokumentNaradi(naradiId))
                .ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceONaradi>);
        }

        public Task<int> UlozitNaradi(int verze, InformaceONaradi data)
        {
            return ProjectorUtils.Save(_folder,
                PrehledCislovanehoNaradiProjection.DokumentNaradi(data.NaradiId),
                verze, JsonSerializer.SerializeToString(data), null);
        }

        public Task<MemoryCacheItem<CislovaneNaradiVPrehledu>> NacistCislovane(Guid naradiId, int cisloNaradi)
        {
            return _folder.GetDocument(PrehledCislovanehoNaradiProjection.DokumentCislovane(naradiId, cisloNaradi))
                .ToMemoryCacheItem(DeserializovatCislovane);
        }

        private static CislovaneNaradiVPrehledu DeserializovatCislovane(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<CislovaneNaradiVPrehledu>(raw);
        }

        public Task<int> UlozitCislovane(int verze, CislovaneNaradiVPrehledu data, bool zobrazitVPrehledu)
        {
            return ProjectorUtils.Save(_folder,
                PrehledCislovanehoNaradiProjection.DokumentCislovane(data.NaradiId, data.CisloNaradi),
                verze, JsonSerializer.SerializeToString(data),
                zobrazitVPrehledu ? new[] { new DocumentIndexing("cisloNaradi", data.CisloNaradi.ToString("00000000")) } : null);
        }

        public Task<Tuple<int, List<CislovaneNaradiVPrehledu>>> NacistSeznamCislovanych(int offset, int pocet)
        {
            return _folder.FindDocuments("cisloNaradi", null, null, offset, pocet, true)
                .ContinueWith(task => Tuple.Create(task.Result.TotalFound, task.Result.Select(c => DeserializovatCislovane(c.Contents)).ToList()));
        }
    }

    public class PrehledCislovanehoNaradiReader
        : IAnswer<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>
    {
        private MemoryCache<PrehledCislovanehoNaradiResponse> _cache;
        private PrehledCislovanehoNaradiRepository _repository;

        public PrehledCislovanehoNaradiReader(PrehledCislovanehoNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<PrehledCislovanehoNaradiResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>(this);
        }

        public Task<PrehledCislovanehoNaradiResponse> Handle(PrehledCislovanehoNaradiRequest message)
        {
            return _cache.Get(message.Stranka.ToString(),
                load => _repository.NacistSeznamCislovanych(message.Stranka * 100 - 100, 100)
                    .ContinueWith(task => VytvoritResponse(message, task.Result.Item1, task.Result.Item2)))
                .ContinueWith(task => task.Result.Value);
        }

        private MemoryCacheItem<PrehledCislovanehoNaradiResponse> VytvoritResponse(PrehledCislovanehoNaradiRequest request, int celkem, List<CislovaneNaradiVPrehledu> seznam)
        {
            return MemoryCacheItem.Create(1, new PrehledCislovanehoNaradiResponse
            {
                Stranka = request.Stranka,
                PocetCelkem = celkem,
                PocetStranek = (celkem + 99) / 100,
                Seznam = seznam
            });
        }
    }
}
