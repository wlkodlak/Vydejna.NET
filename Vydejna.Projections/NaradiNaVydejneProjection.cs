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
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanoNaradiEvent>
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
        private NaradiNaVydejneRepository _repository;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<NaradiNaVydejne> _cacheVydejna;

        public NaradiNaVydejneProjection(NaradiNaVydejneRepository repository, ITime time)
        {
            _repository = repository;
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
            _cacheVydejna = new MemoryCache<NaradiNaVydejne>(time);
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
            _cacheNaradi.Flush(
                () => _cacheVydejna.Flush(message.OnCompleted, message.OnError,
                    save => _repository.UlozitUmistene(save.Version, save.Value, save.Value.PocetCelkem == 0)),
                message.OnError, save => _repository.UlozitDefinici(save.Version, save.Value));
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            _cacheNaradi.Get(
                DokumentDefiniceNaradi(message.NaradiId),
                (verze, naradi) => ZpracovatDefiniciNaradi(message, verze, naradi),
                message.OnError,
                load => _repository.NacistDefinici(message.NaradiId, load.SetLoadedValue, load.LoadingFailed)
                );
        }

        private void ZpracovatDefiniciNaradi(CommandExecution<DefinovanoNaradiEvent> message, int verze, InformaceONaradi naradi)
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
            message.OnCompleted();
        }

        public static string DokumentDefiniceNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public static string DokumentNaradiNaVydejne(Guid naradiId, StavNaradi stavNaradi)
        {
            return string.Concat("vydejna-", naradiId.ToString("N"), "-", stavNaradi.ToString().ToLowerInvariant());
        }

        private void UpravitNaradi(Action onComplete, Action<Exception> onError, Guid naradiId, int cisloNaradi, StavNaradi stavNaradi, int pocetCelkem)
        {
            _cacheNaradi.Get(
                DokumentDefiniceNaradi(naradiId),
                (verzeDefinice, definice) =>
                {
                    _cacheVydejna.Get(
                        DokumentNaradiNaVydejne(naradiId, stavNaradi),
                        (verze, umistene) => UpravitNaradi(onComplete, naradiId, cisloNaradi, stavNaradi, pocetCelkem, definice, verze, umistene),
                        onError,
                        load => _repository.NacistUmistene(naradiId, stavNaradi, load.SetLoadedValue, load.LoadingFailed));
                },
                onError,
                load => _repository.NacistDefinici(naradiId, load.SetLoadedValue, load.LoadingFailed));
        }

        private void UpravitNaradi(Action onComplete, Guid naradiId, int cisloNaradi, StavNaradi stavNaradi, int pocetCelkem, InformaceONaradi definice, int verze, NaradiNaVydejne umistene)
        {
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
                onComplete();
            }
            else if (pocetCelkem > 0)
            {
                umistene.Vykres = definice.Vykres;
                umistene.Rozmer = definice.Rozmer;
                umistene.Druh = definice.Druh;
                if (cisloNaradi == 0)
                    umistene.PocetNecislovanych = pocetCelkem;
                else
                    umistene.SeznamCislovanych.Add(cisloNaradi);
                umistene.PocetCelkem = pocetCelkem;
                _cacheVydejna.Insert(DokumentNaradiNaVydejne(naradiId, stavNaradi), verze, umistene, dirty: true);
                onComplete();
            }
            else
                onComplete();
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, StavNaradi.VPoradku, 1);
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, StavNaradi.VPoradku, 0);
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, message.StavNaradi, 1);
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, StavNaradi.NutnoOpravit, 0);
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, message.StavNaradi, 1);
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, message.CisloNaradi, StavNaradi.Neopravitelne, 0);
        }

        public Task Handle(NecislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, StavNaradi.VPoradku, message.PocetNaNovem);
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, StavNaradi.VPoradku, message.PocetNaPredchozim);
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, message.StavNaradi, message.PocetNaNovem);
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, StavNaradi.NutnoOpravit, message.PocetNaPredchozim);
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, message.StavNaradi, message.PocetNaNovem);
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.NaradiId, 0, StavNaradi.Neopravitelne, message.PocetNaPredchozim);
        }
    }

    public class NaradiNaVydejneRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaVydejneRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistUmistene(Guid naradiId, StavNaradi stavNaradi, Action<int, NaradiNaVydejne> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                NaradiNaVydejneProjection.DokumentNaradiNaVydejne(naradiId, stavNaradi),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaVydejne>(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitUmistene(int verze, NaradiNaVydejne data, bool smazat, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                NaradiNaVydejneProjection.DokumentNaradiNaVydejne(data.NaradiId, data.StavNaradi),
                smazat ? null : JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                smazat ? null : new[] { 
                    new DocumentIndexing("naradiId", data.NaradiId.ToString()),
                    new DocumentIndexing("podleVykresu", string.Concat(data.Vykres, data.Rozmer))
                },
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistDefinici(Guid naradiId, Action<int, InformaceONaradi> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                NaradiNaVydejneProjection.DokumentDefiniceNaradi(naradiId),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceONaradi>(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitDefinici(int verze, InformaceONaradi data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                NaradiNaVydejneProjection.DokumentDefiniceNaradi(data.NaradiId),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistSeznamUmistenych(int offset, int pocet, Action<int, List<NaradiNaVydejne>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("podleVykresu", null, null, offset, pocet, true,
                list => onLoaded(list.TotalFound, VytvoritSeznamUmistenych(list)), onError);
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
            _cache.Get(message.Request.Stranka.ToString(),
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load => _repository.NacistSeznamUmistenych(message.Request.Stranka * 100 - 100, 100,
                    (pocet, seznam) => load.SetLoadedValue(1, VytvoritResponse(message.Request, pocet, seznam)),
                    load.LoadingFailed));
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
