using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NaradiNaVydejneProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
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
        private IDocumentFolder _store;
        private NaradiNaVydejneSerializer _serializer;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<NaradiNaVydejne> _cacheVydejna;

        public NaradiNaVydejneProjection(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _serializer = new NaradiNaVydejneSerializer();
            _cacheNaradi = new MemoryCache<InformaceONaradi>(executor, time);
            _cacheVydejna = new MemoryCache<NaradiNaVydejne>(executor, time);
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return storedVersion == GetVersion() ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _cacheNaradi.Clear();
            _cacheVydejna.Clear();
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            _cacheNaradi.Flush(
                () => _cacheVydejna.Flush(message.OnCompleted, message.OnError, FlushNaradiNaVydejne),
                message.OnError, FlushDefinovanehoNaradi);
        }

        private void FlushNaradiNaVydejne(IMemoryCacheSave<NaradiNaVydejne> save)
        {
            _store.SaveDocument(
                save.Key,
                _serializer.UlozitNaradiNaVydejne(save.Value),
                DocumentStoreVersion.At(save.Version),
                save.Value == null ? null : new[] {
                    new DocumentIndexing("naradiId", save.Value.NaradiId.ToString()),
                    new DocumentIndexing("podleVykresu", string.Concat(save.Value.Vykres, save.Value.Rozmer))
                },
                () => save.SavedAsVersion(save.Version + 1),
                () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                ex => save.SavingFailed(ex));
        }

        private void FlushDefinovanehoNaradi(IMemoryCacheSave<InformaceONaradi> save)
        {
            _store.SaveDocument(
                save.Key,
                _serializer.UlozitDefiniciNaradi(save.Value),
                DocumentStoreVersion.At(save.Version),
                null,
                () => save.SavedAsVersion(save.Version + 1),
                () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                ex => save.SavingFailed(ex));
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            _cacheNaradi.Get(
                DokumentDefiniceNaradi(message.Command.NaradiId),
                (verze, naradi) => ZpracovatDefiniciNaradi(message, verze, naradi),
                message.OnError, NacistCacheDefinice);
        }

        private void ZpracovatDefiniciNaradi(CommandExecution<DefinovanoNaradiEvent> message, int verze, InformaceONaradi naradi)
        {
            if (naradi == null)
            {
                naradi = new InformaceONaradi();
                naradi.NaradiId = message.Command.NaradiId;
            }
            naradi.Vykres = message.Command.Vykres;
            naradi.Rozmer = message.Command.Rozmer;
            naradi.Druh = message.Command.Druh;
            _cacheNaradi.Insert(DokumentDefiniceNaradi(message.Command.NaradiId), verze, naradi, dirty: true);
        }

        private void NacistCacheDefinice(IMemoryCacheLoad<InformaceONaradi> load)
        {
            _store.GetDocument(load.Key,
                (verze, raw) => load.SetLoadedValue(verze, _serializer.NacistDefiniciNaradi(raw)),
                () => load.SetLoadedValue(0, null), load.LoadingFailed);
        }

        private static string DokumentDefiniceNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        private static string DokumentNaradiNaVydejne(Guid naradiId, StavNaradi stavNaradi)
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
                        onError, NacistCacheUmisteni);
                },
                onError, NacistCacheDefinice);
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

                if (umistene.PocetCelkem == 0)
                    umistene = null;
                _cacheVydejna.Insert(DokumentNaradiNaVydejne(naradiId, stavNaradi), verze, null, dirty: true);
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
                _cacheVydejna.Insert(DokumentNaradiNaVydejne(naradiId, stavNaradi), verze, null, dirty: true);
                onComplete();
            }
        }

        private void NacistCacheUmisteni(IMemoryCacheLoad<NaradiNaVydejne> load)
        {
            _store.GetNewerDocument(load.Key, load.OldVersion,
                (verze, raw) => load.SetLoadedValue(verze, _serializer.NacistNaradiNaVydejne(raw)),
                () => load.ValueIsStillValid(),
                () => load.SetLoadedValue(0, null),
                load.LoadingFailed);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.VPoradku, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.VPoradku, 0);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, message.Command.StavNaradi, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.NutnoOpravit, 0);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, message.Command.StavNaradi, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, StavNaradi.Neopravitelne, 0);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.VPoradku, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.VPoradku, message.Command.PocetNaPredchozim);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.StavNaradi, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.NutnoOpravit, message.Command.PocetNaPredchozim);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.StavNaradi, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            UpravitNaradi(message.OnCompleted, message.OnError, message.Command.NaradiId, 0, StavNaradi.Neopravitelne, message.Command.PocetNaPredchozim);
        }
    }

    public class NaradiNaVydejneSerializer
    {
        public NaradiNaVydejne NacistNaradiNaVydejne(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaVydejne>(raw);
            result = result ?? new NaradiNaVydejne();
            return result;
        }

        public string UlozitNaradiNaVydejne(NaradiNaVydejne data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public InformaceONaradi NacistDefiniciNaradi(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceONaradi>(raw);
            result = result ?? new InformaceONaradi();
            return result;
        }

        public string UlozitDefiniciNaradi(InformaceONaradi data)
        {
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class NaradiNaVydejneReader
        : IAnswer<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<ZiskatNaradiNaVydejneResponse> _cache;
        private NaradiNaVydejneSerializer _serializer;

        public NaradiNaVydejneReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<ZiskatNaradiNaVydejneResponse>(executor, time);
            _serializer = new NaradiNaVydejneSerializer();
        }

        public void Handle(QueryExecution<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse> message)
        {
            _cache.Get(message.Request.Stranka.ToString(),
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load =>
                {
                    _store.FindDocuments("podleVykresu", null, null, message.Request.Stranka * 100 - 100, 100, true,
                        list => load.SetLoadedValue(1, VytvoritResponse(message.Request, list)), load.LoadingFailed);
                });
        }

        private ZiskatNaradiNaVydejneResponse VytvoritResponse(ZiskatNaradiNaVydejneRequest request, DocumentStoreFoundDocuments list)
        {
            var response = new ZiskatNaradiNaVydejneResponse();
            response.Stranka = request.Stranka;
            response.PocetCelkem = list.TotalFound;
            response.PocetStranek = (response.PocetCelkem + 99) / 100;
            response.Seznam = list.Select(doc => _serializer.NacistNaradiNaVydejne(doc.Contents)).ToList();
            return response;
        }
    }
}
