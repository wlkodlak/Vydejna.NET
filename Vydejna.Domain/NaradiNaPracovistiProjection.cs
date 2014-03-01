using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ServiceLib;
using ServiceStack.Text;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NaradiNaPracovistiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
        , IHandle<CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
    {
        private IDocumentFolder _store;
        private NaradiNaPracovistiDataCiselniku _cacheCiselniku;
        private int _cacheCiselnikuVersion;
        private bool _cacheCiselnikuDirty;
        private MemoryCache<NaradiNaPracovistiDataPracoviste> _cachePracovist;
        private NaradiNaPracovistiSerializer _serializer;

        public NaradiNaPracovistiProjection(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cachePracovist = new MemoryCache<NaradiNaPracovistiDataPracoviste>(executor, time);
            _serializer = new NaradiNaPracovistiSerializer();
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
            _cachePracovist.Clear();
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
            private NaradiNaPracovistiProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushExecutor(NaradiNaPracovistiProjection parent, Action onComplete, Action<Exception> onError)
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
                    UlozitPracoviste();
            }

            private void UlozitCiselnik()
            {
                _parent._store.SaveDocument(
                    "ciselnik",
                    _parent._serializer.UlozitCiselnik(_parent._cacheCiselniku),
                    DocumentStoreVersion.At(_parent._cacheCiselnikuVersion),
                    null,
                    () => { _parent._cacheCiselnikuVersion++; UlozitPracoviste(); },
                    () => _onError(new ProjectorMessages.ConcurrencyException()),
                    _onError);
            }

            private void UlozitPracoviste()
            {
                _parent._cachePracovist.Flush(_onComplete, _onError, save =>
                {
                    _parent._store.SaveDocument(
                        save.Key,
                        _parent._serializer.UlozitPracoviste(save.Value),
                        DocumentStoreVersion.At(save.Version),
                        null,
                        () => save.SavedAsVersion(save.Version + 1),
                        () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                        save.SavingFailed
                        );
                });
            }
        }

        private void UpravitPracoviste(string kodPracoviste, Action onComplete, Action<Exception> onError, Action<NaradiNaPracovistiDataPracoviste> akce)
        {
            _cachePracovist.Get(kodPracoviste,
                (verze, data) =>
                {
                    akce(data);
                    _cachePracovist.Insert(kodPracoviste, verze, data, dirty: true);
                    onComplete();
                },
                onError,
                load =>
                {
                    if (load.OldValueAvailable)
                    {
                        _store.GetNewerDocument(load.Key, load.OldVersion,
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.NacistPracovisteKompletni(raw)),
                            () => load.ValueIsStillValid(),
                            () => load.SetLoadedValue(0, _serializer.NacistPracovisteKompletni(null)),
                            load.LoadingFailed);
                    }
                    else
                    {
                        _store.GetDocument(load.Key,
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.NacistPracovisteKompletni(raw)),
                            () => load.SetLoadedValue(0, _serializer.NacistPracovisteKompletni(null)),
                            load.LoadingFailed);
                    }
                });
        }

        private void UpravitNaradiNaPracovisti(string kodPracoviste, Action onComplete, Action<Exception> onError, Guid naradiId, int cisloNaradi, int zmenaPoctu, DateTime? datumVydeje, int verzeNaradi)
        {
            UpravitPracoviste(kodPracoviste, onComplete, onError, data => {
                NaradiNaPracovisti naradiNaPracovisti;
                InformaceONaradi naradiInfo;
                if (!data.IndexPodleIdNaradi.TryGetValue(naradiId, out naradiNaPracovisti))
                {
                    data.IndexPodleIdNaradi[naradiId] = naradiNaPracovisti = new NaradiNaPracovisti();
                    naradiNaPracovisti.NaradiId = naradiId;
                }
                if (_cacheCiselniku.IndexPodleId.TryGetValue(naradiId, out naradiInfo))
                {
                    naradiNaPracovisti.Vykres = naradiInfo.Vykres;
                    naradiNaPracovisti.Rozmer = naradiInfo.Rozmer;
                    naradiNaPracovisti.Druh = naradiInfo.Druh;
                }

                if (cisloNaradi == 0)
                {
                    if (naradiNaPracovisti.VerzeNaradi < verzeNaradi)
                        naradiNaPracovisti.VerzeNaradi = verzeNaradi;
                    else
                        return;
                }
                else
                {
                    if (naradiNaPracovisti.VerzeNaradi < verzeNaradi)
                        naradiNaPracovisti.VerzeNaradi = verzeNaradi;
                    else
                        return;
                }

                if (datumVydeje.HasValue)
                    naradiNaPracovisti.DatumPoslednihoVydeje = datumVydeje.Value;
                data.PocetCelkem += zmenaPoctu;
                naradiNaPracovisti.PocetCelkem += zmenaPoctu;
                if (cisloNaradi > 0)
                {
                    naradiNaPracovisti.PocetNecislovanych += zmenaPoctu;
                }
                else
                {
                    if (zmenaPoctu > 0)
                        naradiNaPracovisti.SeznamCislovanych.Add(cisloNaradi);
                    else if (zmenaPoctu < 0)
                        naradiNaPracovisti.SeznamCislovanych.Remove(cisloNaradi);
                }
            });
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            UpravitPracoviste(message.Command.Kod, message.OnCompleted, message.OnError, data =>
            {
                data.Pracoviste = new InformaceOPracovisti
                {
                    Kod = message.Command.Kod,
                    Nazev = message.Command.Nazev,
                    Stredisko = message.Command.Stredisko,
                    Aktivni = !message.Command.Deaktivovano
                };
            });
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradiNaPracovisti(message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, 1, message.Command.Datum, message.Command.Verze);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradiNaPracovisti(message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, message.Command.CisloNaradi, -1, null, message.Command.Verze);
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            UpravitNaradiNaPracovisti(message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, 0, message.Command.Pocet, message.Command.Datum, message.Command.Verze);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            UpravitNaradiNaPracovisti(message.Command.KodPracoviste, message.OnCompleted, message.OnError, message.Command.NaradiId, 0, -message.Command.Pocet, null, message.Command.Verze);
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            InformaceONaradi naradiInfo;
            if (!_cacheCiselniku.IndexPodleId.TryGetValue(message.Command.NaradiId, out naradiInfo))
            {
                _cacheCiselniku.IndexPodleId[message.Command.NaradiId] = naradiInfo = new InformaceONaradi();
                naradiInfo.Vykres = message.Command.Vykres;
                naradiInfo.Rozmer = message.Command.Rozmer;
                naradiInfo.Druh = message.Command.Druh;
                _cacheCiselnikuDirty = true;
            }
            message.OnCompleted();
        }
    }

    public class NaradiNaPracovistiSerializer
    {
        public NaradiNaPracovistiDataPracoviste NacistPracovisteProReader(string raw)
        {
            return JsonSerializer.DeserializeFromString<NaradiNaPracovistiDataPracoviste>(raw);
        }

        public NaradiNaPracovistiDataPracoviste NacistPracovisteKompletni(string raw)
        {
            var result = JsonSerializer.DeserializeFromString<NaradiNaPracovistiDataPracoviste>(raw);
            result = result ?? new NaradiNaPracovistiDataPracoviste();
            result.Pracoviste = result.Pracoviste ?? new InformaceOPracovisti();
            result.Seznam = result.Seznam ?? new List<NaradiNaPracovisti>();
            result.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaPracovisti>(result.Seznam.Count);
            foreach (var naradi in result.Seznam)
                result.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
            return result;
        }

        public NaradiNaPracovistiDataCiselniku NacistCiselnik(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaPracovistiDataCiselniku>(raw);
            result = result ?? new NaradiNaPracovistiDataCiselniku();
            result.Seznam = new List<InformaceONaradi>();
            result.IndexPodleId = new Dictionary<Guid, InformaceONaradi>(result.Seznam.Count);
            foreach (var pracoviste in result.Seznam)
                result.IndexPodleId[pracoviste.NaradiId] = pracoviste;
            return result;
        }

        public string UlozitCiselnik(NaradiNaPracovistiDataCiselniku data)
        {
            data.Seznam = data.IndexPodleId.Values.ToList();
            return JsonSerializer.SerializeToString(data);
        }

        public string UlozitPracoviste(NaradiNaPracovistiDataPracoviste data)
        {
            data.Seznam = data.IndexPodleIdNaradi.Values.ToList();
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class NaradiNaPracovistiDataPracoviste
    {
        public InformaceOPracovisti Pracoviste { get; set; }
        public int PocetCelkem { get; set; }
        public List<NaradiNaPracovisti> Seznam { get; set; }
        public Dictionary<Guid, NaradiNaPracovisti> IndexPodleIdNaradi;
    }
    public class NaradiNaPracovistiDataCiselniku
    {
        public List<InformaceONaradi> Seznam { get; set; }
        public Dictionary<Guid, InformaceONaradi> IndexPodleId;
    }

    public class NaradiNaPracovistiReader
        : IAnswer<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<ZiskatNaradiNaPracovistiResponse> _cache;
        private NaradiNaPracovistiSerializer _serializer;

        public NaradiNaPracovistiReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<ZiskatNaradiNaPracovistiResponse>(executor, time);
            _serializer = new NaradiNaPracovistiSerializer();
        }

        public void Handle(QueryExecution<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse> message)
        {
            _cache.Get(message.Request.KodPracoviste, (verze, data) => message.OnCompleted(data), message.OnError, NacistPracoviste);
        }

        private void NacistPracoviste(IMemoryCacheLoad<ZiskatNaradiNaPracovistiResponse> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(load.Key, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(load.Key, raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponse(load.Key, null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument(load.Key,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(load.Key, raw)),
                    () => load.SetLoadedValue(0, VytvoritResponse(load.Key, null)),
                    load.LoadingFailed);
            }
        }

        private ZiskatNaradiNaPracovistiResponse VytvoritResponse(string kodPracoviste, string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return NenalezenePracoviste(kodPracoviste);
            var zaklad = _serializer.NacistPracovisteProReader(raw);
            if (zaklad == null)
                return NenalezenePracoviste(kodPracoviste);
            else
                return NalezenePracoviste(zaklad);
        }

        private ZiskatNaradiNaPracovistiResponse NalezenePracoviste(NaradiNaPracovistiDataPracoviste zaklad)
        {
            return new ZiskatNaradiNaPracovistiResponse
            {
                Pracoviste = zaklad.Pracoviste,
                PracovisteExistuje = true,
                Seznam = zaklad.Seznam,
                PocetCelkem = zaklad.PocetCelkem
            };
        }

        private static ZiskatNaradiNaPracovistiResponse NenalezenePracoviste(string kodPracoviste)
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

    }
}
