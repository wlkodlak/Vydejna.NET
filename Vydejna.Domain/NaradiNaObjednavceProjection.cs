using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NaradiNaObjednavceProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
    {
        private IDocumentFolder _store;
        private NaradiNaObjednavceDataCiselniku _cacheCiselniku;
        private int _cacheCiselnikuVersion;
        private bool _cacheCiselnikuDirty;
        private MemoryCache<NaradiNaObjednavceDataObjednavky> _cacheObjednavek;
        private NaradiNaObjednavceSerializer _serializer;

        public NaradiNaObjednavceProjection(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cacheObjednavek = new MemoryCache<NaradiNaObjednavceDataObjednavky>(executor, time);
            _serializer = new NaradiNaObjednavceSerializer();
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
            _cacheCiselniku = _serializer.NacistCiselnik(null);
            _cacheCiselnikuDirty = false;
            _cacheCiselnikuVersion = 0;
            _cacheObjednavek.Clear();
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument("ciselnik",
                (verze, raw) =>
                {
                    _cacheCiselnikuVersion = verze;
                    _cacheCiselnikuDirty = false;
                    _cacheCiselniku = _serializer.NacistCiselnik(raw);
                    message.OnCompleted();
                },
                () =>
                {
                    _cacheCiselnikuVersion = 0;
                    _cacheCiselnikuDirty = false;
                    _cacheCiselniku = _serializer.NacistCiselnik(null);
                },
                message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushExecutor(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushExecutor
        {
            private NaradiNaObjednavceProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushExecutor(NaradiNaObjednavceProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                if (_parent._cacheCiselnikuDirty)
                    UlozitCiselnik();
                else
                    UlozitObjednavky();
            }

            private void UlozitCiselnik()
            {
                _parent._store.SaveDocument(
                    "ciselnik",
                    _parent._serializer.UlozitCiselnik(_parent._cacheCiselniku),
                    DocumentStoreVersion.At(_parent._cacheCiselnikuVersion),
                    null,
                    () => { _parent._cacheCiselnikuVersion++; UlozitObjednavky(); },
                    () => _onError(new ProjectorMessages.ConcurrencyException()),
                    _onError);
            }

            private void UlozitObjednavky()
            {
                _parent._cacheObjednavek.Flush(_onComplete, _onError, save =>
                {
                    _parent._store.SaveDocument(
                        save.Key,
                        _parent._serializer.UlozitObjednavku(save.Value),
                        DocumentStoreVersion.At(save.Version),
                        new[] { new DocumentIndexing("dodavatel", new[] { save.Value.Dodavatel.Kod }) },
                        () => save.SavedAsVersion(save.Version + 1),
                        () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                        save.SavingFailed
                        );
                });
            }
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            InformaceONaradi naradiInfo;
            if (!_cacheCiselniku.IndexNaradi.TryGetValue(message.Command.NaradiId, out naradiInfo))
            {
                _cacheCiselniku.IndexNaradi[message.Command.NaradiId] = naradiInfo = new InformaceONaradi();
                naradiInfo.Vykres = message.Command.Vykres;
                naradiInfo.Rozmer = message.Command.Rozmer;
                naradiInfo.Druh = message.Command.Druh;
                _cacheCiselnikuDirty = true;
            }
            message.OnCompleted();
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            InformaceODodavateli dodavatelInfo;
            if (!_cacheCiselniku.IndexDodavatelu.TryGetValue(message.Command.Kod, out dodavatelInfo))
            {
                _cacheCiselniku.IndexDodavatelu[message.Command.Kod] = dodavatelInfo = new InformaceODodavateli();
                dodavatelInfo.Nazev = message.Command.Nazev;
                dodavatelInfo.Adresa = message.Command.Adresa;
                dodavatelInfo.Ico = message.Command.Ico;
                dodavatelInfo.Dic = message.Command.Dic;
                dodavatelInfo.Aktivni = !message.Command.Deaktivovan;
                _cacheCiselnikuDirty = true;
            }
            message.OnCompleted();
        }

        private void UpravitObjednavku(string kodDodavatele, string cisloObjednavky, Action onComplete, Action<Exception> onError,
            Guid naradiId, int cisloNaradi, int novyPocet)
        {
            var nazevDokumentu = _serializer.NazevDokumentuObjednavky(kodDodavatele, cisloObjednavky);
            _cacheObjednavek.Get(
                nazevDokumentu,
                (verze, data) =>
                {
                    data.Objednavka = cisloObjednavky;
                    NaradiNaObjednavce naradiNaObjednavce;
                    InformaceODodavateli dodavatelInfo;
                    InformaceONaradi naradiInfo;

                    if (_cacheCiselniku.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatelInfo))
                        data.Dodavatel = dodavatelInfo;
                    else if (data.Dodavatel == null)
                        data.Dodavatel = new InformaceODodavateli { Kod = kodDodavatele };

                    if (!data.IndexPodleIdNaradi.TryGetValue(naradiId, out naradiNaObjednavce))
                    {
                        data.IndexPodleIdNaradi[naradiId] = naradiNaObjednavce = new NaradiNaObjednavce();
                        naradiNaObjednavce.NaradiId = naradiId;
                    }
                    if (_cacheCiselniku.IndexNaradi.TryGetValue(naradiId, out naradiInfo))
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
                    data.PocetCelkem = data.IndexPodleIdNaradi.Values.Sum(n => n.PocetCelkem);
                    _cacheObjednavek.Insert(nazevDokumentu, verze, data, dirty: true);
                    onComplete();
                },
                onError,
                load =>
                {
                    _store.GetDocument(load.Key,
                        (verze, raw) => load.SetLoadedValue(verze, _serializer.NacistObjednavkuKompletni(raw)),
                        () => load.SetLoadedValue(0, _serializer.NacistObjednavkuKompletni(null)),
                        load.LoadingFailed);
                });
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitObjednavku(message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, message.Command.CisloNaradi, 1);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitObjednavku(message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, message.Command.CisloNaradi, 0);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitObjednavku(message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, 0, message.Command.PocetNaNovem);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitObjednavku(message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, 0, message.Command.PocetNaPredchozim);
        }
    }

    public class NaradiNaObjednavceSerializer
    {
        public NaradiNaObjednavceDataObjednavky NacistObjednavkuProReader(string raw)
        {
            return JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataObjednavky>(raw);
        }

        public NaradiNaObjednavceDataObjednavky NacistObjednavkuKompletni(string raw)
        {
            var result = JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataObjednavky>(raw);
            result = result ?? new NaradiNaObjednavceDataObjednavky();
            result.Dodavatel = result.Dodavatel ?? new InformaceODodavateli();
            result.Seznam = result.Seznam ?? new List<NaradiNaObjednavce>();
            result.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaObjednavce>(result.Seznam.Count);
            foreach (var naradi in result.Seznam)
                result.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
            return result;
        }

        public NaradiNaObjednavceDataCiselniku NacistCiselnik(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataCiselniku>(raw);
            result = result ?? new NaradiNaObjednavceDataCiselniku();
            result.SeznamNaradi = new List<InformaceONaradi>();
            result.SeznamDodavatelu = new List<InformaceODodavateli>();
            result.IndexNaradi = new Dictionary<Guid, InformaceONaradi>(result.SeznamNaradi.Count);
            result.IndexDodavatelu = new Dictionary<string, InformaceODodavateli>(result.SeznamDodavatelu.Count);
            foreach (var pracoviste in result.SeznamNaradi)
                result.IndexNaradi[pracoviste.NaradiId] = pracoviste;
            foreach (var dodavatel in result.SeznamDodavatelu)
                result.IndexDodavatelu[dodavatel.Kod] = dodavatel;
            return result;
        }

        public string UlozitCiselnik(NaradiNaObjednavceDataCiselniku data)
        {
            data.SeznamNaradi = data.IndexNaradi.Values.ToList();
            data.SeznamDodavatelu = data.IndexDodavatelu.Values.ToList();
            return JsonSerializer.SerializeToString(data);
        }

        public string UlozitObjednavku(NaradiNaObjednavceDataObjednavky data)
        {
            data.Seznam = data.IndexPodleIdNaradi.Values.Where(n => n.PocetCelkem > 0).ToList();
            return JsonSerializer.SerializeToString(data);
        }

        public string NazevDokumentuObjednavky(string dodavatel, string objednavka)
        {
            var vystup = new char[dodavatel.Length + objednavka.Length + 1];
            int pozice = 0;
            for (int i = 0; i < dodavatel.Length; i++, pozice++)
            {
                char znak = char.ToUpper(dodavatel[i]);
                if (znak >= 'A' && znak <= 'Z')
                    vystup[pozice] = znak;
                else if (znak >= '0' && znak <= '9')
                    vystup[pozice] = znak;
                else
                    vystup[pozice] = '_';
            }
            vystup[pozice] = '-';
            pozice++;
            for (int i = 0; i < objednavka.Length; i++, pozice++)
            {
                char znak = char.ToUpper(objednavka[i]);
                if (znak >= 'A' && znak <= 'Z')
                    vystup[pozice] = znak;
                else if (znak >= '0' && znak <= '9')
                    vystup[pozice] = znak;
                else
                    vystup[pozice] = '_';
            }
            return new string(vystup);
        }
    }

    public class NaradiNaObjednavceDataObjednavky
    {
        public InformaceODodavateli Dodavatel { get; set; }
        public string Objednavka { get; set; }
        public DateTime? TerminDodani { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaObjednavce> Seznam { get; set; }
        public Dictionary<Guid, NaradiNaObjednavce> IndexPodleIdNaradi;
    }
    public class NaradiNaObjednavceDataCiselniku
    {
        public List<InformaceONaradi> SeznamNaradi { get; set; }
        public List<InformaceODodavateli> SeznamDodavatelu { get; set; }
        public Dictionary<Guid, InformaceONaradi> IndexNaradi;
        public Dictionary<string, InformaceODodavateli> IndexDodavatelu;
    }

    public class NaradiNaObjednavceReader
       : IAnswer<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<ZiskatNaradiNaObjednavceResponse> _cache;
        private NaradiNaObjednavceSerializer _serializer;

        public NaradiNaObjednavceReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<ZiskatNaradiNaObjednavceResponse>(executor, time);
            _serializer = new NaradiNaObjednavceSerializer();
        }

        public void Handle(QueryExecution<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse> message)
        {
            var klic = _serializer.NazevDokumentuObjednavky(message.Request.KodDodavatele, message.Request.Objednavka);
            _cache.Get(
                klic,
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load => NacistPracoviste(message.Request.KodDodavatele, message.Request.Objednavka, load));
        }

        private void NacistPracoviste(string kodDodavatele, string cisloObjednavky, IMemoryCacheLoad<ZiskatNaradiNaObjednavceResponse> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(load.Key, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(kodDodavatele, cisloObjednavky, raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponse(kodDodavatele, cisloObjednavky, null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument(load.Key,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(kodDodavatele, cisloObjednavky, raw)),
                    () => load.SetLoadedValue(0, VytvoritResponse(kodDodavatele, cisloObjednavky, null)),
                    load.LoadingFailed);
            }
        }

        private ZiskatNaradiNaObjednavceResponse VytvoritResponse(string kodDodavatele, string cisloObjednavky, string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return NenalezenaObjednavka(kodDodavatele, cisloObjednavky);
            var zaklad = _serializer.NacistObjednavkuProReader(raw);
            if (zaklad == null)
                return NenalezenaObjednavka(kodDodavatele, cisloObjednavky);
            else
                return NalezenaObjednavka(zaklad);
        }

        private ZiskatNaradiNaObjednavceResponse NalezenaObjednavka(NaradiNaObjednavceDataObjednavky zaklad)
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

        private static ZiskatNaradiNaObjednavceResponse NenalezenaObjednavka(string kodDodavatele, string cisloObjednavky)
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
    }
}
