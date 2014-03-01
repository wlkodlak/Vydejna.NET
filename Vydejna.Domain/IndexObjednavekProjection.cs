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
    public class IndexObjednavekProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
    {
        private IDocumentFolder _store;
        private int _cacheDodavateluVersion;
        private IndexObjednavekDodavatele _cacheDodavatelu;
        private bool _zmenenyDodavatele;
        private MemoryCache<IndexObjednavekDataObjednavek> _cacheObjednavek;
        private MemoryCache<IndexObjednavekDataDodacichListu> _cacheDodacichListu;
        private IndexObjednavekSerializer _serializer;

        public IndexObjednavekProjection(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cacheDodavatelu = (IndexObjednavekDodavatele)null;
            _cacheObjednavek = new MemoryCache<IndexObjednavekDataObjednavek>(executor, time);
            _cacheDodacichListu = new MemoryCache<IndexObjednavekDataDodacichListu>(executor, time);
            _serializer = new IndexObjednavekSerializer();
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return GetVersion() == storedVersion ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _cacheDodavateluVersion = 0;
            _cacheObjednavek.Clear();
            _cacheDodacichListu.Clear();
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument("dodavatele",
                (verze, data) =>
                {
                    _cacheDodavateluVersion = verze;
                    _cacheDodavatelu = _serializer.CistDodavatele(data);
                    message.OnCompleted();
                },
                () =>
                {
                    _cacheDodavateluVersion = 0;
                    _cacheDodavatelu = _serializer.CistDodavatele(null);
                    message.OnCompleted();
                },
                message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new UlozitZmeny(this, message.OnCompleted, message.OnError).Execute();
        }

        private class UlozitZmeny
        {
            private Action _onComplete;
            private Action<Exception> _onError;
            private IndexObjednavekProjection _parent;

            public UlozitZmeny(IndexObjednavekProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                if (_parent._zmenenyDodavatele)
                    UlozitDodavatele();
                UlozitObjednavky();
            }

            private void UlozitDodavatele()
            {
                _parent._store.SaveDocument(
                    "dodavatele",
                    _parent._serializer.ZapsatDodavatele(_parent._cacheDodavatelu),
                    DocumentStoreVersion.At(_parent._cacheDodavateluVersion),
                    null,
                    UlozitObjednavky,
                    () => _onError(new ProjectorMessages.ConcurrencyException()),
                    _onError);
            }

            private void UlozitObjednavky()
            {
                _parent._cacheObjednavek.Flush(UlozitDodaciListy, _onError, save =>
                {
                    _parent._store.SaveDocument(
                        string.Concat("objednavka-", save.Key),
                        _parent._serializer.ZapsatObjednavku(save.Value),
                        DocumentStoreVersion.At(save.Version),
                        null,
                        () => save.SavedAsVersion(save.Version + 1),
                        () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                        save.SavingFailed);
                });
            }

            private void UlozitDodaciListy()
            {
                _parent._cacheDodacichListu.Flush(_onComplete, _onError, save =>
                {
                    _parent._store.SaveDocument(
                        string.Concat("dodacilist-", save.Key),
                        _parent._serializer.ZapsatDodaciList(save.Value),
                        DocumentStoreVersion.At(save.Version),
                        null,
                        () => save.SavedAsVersion(save.Version + 1),
                        () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                        save.SavingFailed);
                });
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            ZpracovatObjednavku(message.OnCompleted, message.OnError, message.Command.Objednavka, message.Command.KodDodavatele, message.Command.TerminDodani);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            ZpracovatObjednavku(message.OnCompleted, message.OnError, message.Command.Objednavka, message.Command.KodDodavatele, message.Command.TerminDodani);
        }

        private void ZpracovatObjednavku(Action onCompleted, Action<Exception> onError, string cisloObjednavky, string kodDodavatele, DateTime terminDodani)
        {
            _cacheObjednavek.Get(cisloObjednavky,
                (verze, objednavky) =>
                {
                    if (objednavky == null)
                    {
                        objednavky = new IndexObjednavekDataObjednavek();
                        objednavky.CisloObjednavky = cisloObjednavky;
                    }
                    var existujici = objednavky.Kandidati.FirstOrDefault(k => k.KodDodavatele == kodDodavatele);
                    if (existujici == null)
                    {
                        existujici = new NalezenaObjednavka();
                        existujici.Objednavka = cisloObjednavky;
                        existujici.KodDodavatele = kodDodavatele;
                    }
                    existujici.NazevDodavatele = _cacheDodavatelu.Dodavatele.Where(d => d.Kod == kodDodavatele).Select(d => d.Nazev).FirstOrDefault();
                    existujici.TerminDodani = terminDodani;
                    _cacheObjednavek.Insert(cisloObjednavky, verze, objednavky, dirty: true);
                    onCompleted();
                },
                onError,
                load =>
                {
                    if (load.OldValueAvailable)
                    {
                        _store.GetNewerDocument(string.Concat("objednavka-", cisloObjednavky), load.OldVersion,
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.CistObjednavku(raw)),
                            () => load.ValueIsStillValid(),
                            () => load.SetLoadedValue(0, _serializer.CistObjednavku(null)),
                            load.LoadingFailed);
                    }
                    else
                    {
                        _store.GetDocument(string.Concat("objednavka-", cisloObjednavky),
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.CistObjednavku(raw)),
                            () => load.SetLoadedValue(0, _serializer.CistObjednavku(null)),
                            load.LoadingFailed);
                    }
                });
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            ZpracovatDodaciList(message.OnCompleted, message.OnError, message.Command.DodaciList, message.Command.KodDodavatele, message.Command.Objednavka);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            ZpracovatDodaciList(message.OnCompleted, message.OnError, message.Command.DodaciList, message.Command.KodDodavatele, message.Command.Objednavka);
        }

        private void ZpracovatDodaciList(Action onCompleted, Action<Exception> onError, string dodaciList, string kodDodavatele, string cisloObjednavky)
        {
            _cacheDodacichListu.Get(dodaciList,
                (verze, dodaciListy) =>
                {
                    if (dodaciListy == null)
                    {
                        dodaciListy = new IndexObjednavekDataDodacichListu();
                        dodaciListy.CisloDodacihoListu = dodaciList;
                    }
                    var existujici = dodaciListy.Kandidati.FirstOrDefault(k => k.KodDodavatele == kodDodavatele);
                    if (existujici == null)
                    {
                        existujici = new NalezenyDodaciList();
                        existujici.DodaciList = cisloObjednavky;
                        existujici.KodDodavatele = kodDodavatele;
                    }
                    existujici.Objednavky = existujici.Objednavky ?? new List<string>();
                    existujici.NazevDodavatele = _cacheDodavatelu.Dodavatele.Where(d => d.Kod == kodDodavatele).Select(d => d.Nazev).FirstOrDefault();
                    if (!existujici.Objednavky.Contains(cisloObjednavky))
                        existujici.Objednavky.Add(cisloObjednavky);
                    _cacheDodacichListu.Insert(dodaciList, verze, dodaciListy, dirty: true);
                    onCompleted();
                },
                onError,
                load =>
                {
                    if (load.OldValueAvailable)
                    {
                        _store.GetNewerDocument(string.Concat("dodacilist-", cisloObjednavky), load.OldVersion,
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.CistDodaciList(raw)),
                            () => load.ValueIsStillValid(),
                            () => load.SetLoadedValue(0, _serializer.CistDodaciList(null)),
                            load.LoadingFailed);
                    }
                    else
                    {
                        _store.GetDocument(string.Concat("dodacilist-", cisloObjednavky),
                            (verze, raw) => load.SetLoadedValue(verze, _serializer.CistDodaciList(raw)),
                            () => load.SetLoadedValue(0, _serializer.CistDodaciList(null)),
                            load.LoadingFailed);
                    }
                });
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            var existujici = _cacheDodavatelu.Dodavatele.FirstOrDefault(d => d.Kod == message.Command.Kod);
            if (existujici == null)
            {
                existujici = new IndexObjednavekDodavatel();
                existujici.Kod = message.Command.Kod;
                _cacheDodavatelu.Dodavatele.Add(existujici);
            }
            existujici.Nazev = message.Command.Nazev;
            _zmenenyDodavatele = true;
        }
    }

    public class IndexObjednavekSerializer
    {
        public IndexObjednavekDataObjednavek ObjednavkaProReader(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<IndexObjednavekDataObjednavek>(raw);
            result = result ?? new IndexObjednavekDataObjednavek();
            result.Kandidati = result.Kandidati ?? new List<NalezenaObjednavka>();
            return result;
        }

        public IndexObjednavekDataDodacichListu DodaciListProReader(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<IndexObjednavekDataDodacichListu>(raw);
            result = result ?? new IndexObjednavekDataDodacichListu();
            result.Kandidati = result.Kandidati ?? new List<NalezenyDodaciList>();
            return result;
        }

        public IndexObjednavekDodavatele CistDodavatele(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<IndexObjednavekDodavatele>(raw);
            result = result ?? new IndexObjednavekDodavatele();
            result.Dodavatele = result.Dodavatele ?? new List<IndexObjednavekDodavatel>();
            return result;
        }

        public IndexObjednavekDataObjednavek CistObjednavku(string raw)
        {
            return ObjednavkaProReader(raw);
        }

        public IndexObjednavekDataDodacichListu CistDodaciList(string raw)
        {
            return DodaciListProReader(raw);
        }

        public string ZapsatDodavatele(IndexObjednavekDodavatele data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public string ZapsatObjednavku(IndexObjednavekDataObjednavek data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public string ZapsatDodaciList(IndexObjednavekDataDodacichListu data)
        {
            return JsonSerializer.SerializeToString(data);
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
        private IDocumentFolder _store;
        private IndexObjednavekSerializer _serializer;

        public IndexObjednavekReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cacheObjednavek = new MemoryCache<NajitObjednavkuResponse>(executor, time);
            _cacheDodacichListu = new MemoryCache<NajitDodaciListResponse>(executor, time);
            _serializer = new IndexObjednavekSerializer();
        }

        public void Handle(QueryExecution<NajitObjednavkuRequest, NajitObjednavkuResponse> message)
        {
            _cacheObjednavek.Get(message.Request.Objednavka, (verze, data) => message.OnCompleted(data), message.OnError, NacistObjednavku);
        }

        private void NacistObjednavku(IMemoryCacheLoad<NajitObjednavkuResponse> load)
        {
            var nazevDokumentu = string.Concat("objednavka-", load.Key);
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(nazevDokumentu, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponseObjednavky(load.Key, raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponseObjednavky(load.Key, null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument(nazevDokumentu,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponseObjednavky(load.Key, raw)),
                    () => load.SetLoadedValue(0, VytvoritResponseObjednavky(load.Key, null)),
                    load.LoadingFailed);
            }
        }

        private NajitObjednavkuResponse VytvoritResponseObjednavky(string cisloObjednavky, string raw)
        {
            var zaklad = _serializer.ObjednavkaProReader(raw);
            return new NajitObjednavkuResponse
            {
                Objednavka = cisloObjednavky,
                Nalezena = !string.IsNullOrEmpty(raw),
                Kandidati = zaklad.Kandidati
            };
        }

        public void Handle(QueryExecution<NajitDodaciListRequest, NajitDodaciListResponse> message)
        {
            _cacheDodacichListu.Get(message.Request.DodaciList, (verze, data) => message.OnCompleted(data), message.OnError, NacistDodaciList);
        }

        private void NacistDodaciList(IMemoryCacheLoad<NajitDodaciListResponse> load)
        {
            var nazevDokumentu = string.Concat("dodacilist-", load.Key);
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(nazevDokumentu, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponseDodacihoListu(load.Key, raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponseDodacihoListu(load.Key, null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument(nazevDokumentu,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponseDodacihoListu(load.Key, raw)),
                    () => load.SetLoadedValue(0, VytvoritResponseDodacihoListu(load.Key, null)),
                    load.LoadingFailed);
            }
        }

        private NajitDodaciListResponse VytvoritResponseDodacihoListu(string p, string raw)
        {
            var zaklad = _serializer.DodaciListProReader(raw);
            return new NajitDodaciListResponse
            {
                DodaciList = p,
                Nalezen = !string.IsNullOrEmpty(raw),
                Kandidati = zaklad.Kandidati
            };
        }
    }
}
