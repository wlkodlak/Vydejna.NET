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
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
    {
        private IndexObjednavekRepository _repository;
        private MemoryCache<IndexObjednavekDodavatele> _cacheDodavatelu;
        private MemoryCache<IndexObjednavekDataObjednavek> _cacheObjednavek;
        private MemoryCache<IndexObjednavekDataDodacichListu> _cacheDodacichListu;

        public IndexObjednavekProjection(IndexObjednavekRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheDodavatelu = new MemoryCache<IndexObjednavekDodavatele>(executor, time);
            _cacheObjednavek = new MemoryCache<IndexObjednavekDataObjednavek>(executor, time);
            _cacheDodacichListu = new MemoryCache<IndexObjednavekDataDodacichListu>(executor, time);
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
            _cacheDodavatelu.Clear();
            _cacheObjednavek.Clear();
            _cacheDodacichListu.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
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
                _parent._cacheDodavatelu.Flush(UlozitObjednavky, _onError,
                    save => _parent._repository.UlozitDodavatele(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void UlozitObjednavky()
            {
                _parent._cacheObjednavek.Flush(UlozitDodaciListy, _onError,
                    save => _parent._repository.UlozitObjednavku(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void UlozitDodaciListy()
            {
                _parent._cacheDodacichListu.Flush(_onComplete, _onError,
                    save => _parent._repository.UlozitDodaciList(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            new ZpracovatObjednavku(this, message.OnCompleted, message.OnError, message.Command.Objednavka, message.Command.KodDodavatele, message.Command.TerminDodani).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            new ZpracovatObjednavku(this, message.OnCompleted, message.OnError, message.Command.Objednavka, message.Command.KodDodavatele, message.Command.TerminDodani).Execute();
        }

        private class ZpracovatObjednavku
        {
            private IndexObjednavekProjection _parent;
            private Action _onCompleted;
            private Action<Exception> _onError;
            private string _cisloObjednavky;
            private string _kodDodavatele;
            private DateTime _terminDodani;

            private int _verzeObjednavky;
            private IndexObjednavekDataObjednavek _objednavka;
            private IndexObjednavekDodavatele _dodavatele;

            public ZpracovatObjednavku(IndexObjednavekProjection parent, Action onCompleted, Action<Exception> onError, string cisloObjednavky, string kodDodavatele, DateTime terminDodani)
            {
                _parent = parent;
                _onCompleted = onCompleted;
                _onError = onError;
                _cisloObjednavky = cisloObjednavky;
                _kodDodavatele = kodDodavatele;
                _terminDodani = terminDodani;
            }

            public void Execute()
            {
                _parent._cacheObjednavek.Get(
                    _cisloObjednavky,
                    (verze, objednavka) => NactenaObjednavka(verze, objednavka),
                    ex => _onError(ex),
                    load => _parent._repository.NacistObjednavku(_cisloObjednavky, load.OldVersion, load.SetLoadedValue, load.ValueIsStillValid, load.LoadingFailed)
                    );
            }

            private void NactenaObjednavka(int verzeObjednavky, IndexObjednavekDataObjednavek objednavka)
            {
                _verzeObjednavky = verzeObjednavky;
                _objednavka = objednavka;

                _parent._cacheDodavatelu.Get(
                    "dodavatele",
                    (verze, data) => NacteniDodavatele(verze, data),
                    ex => _onError(ex),
                    load => _parent._repository.NacistDodavatele(
                        (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                        load.LoadingFailed)
                    );
            }

            private void NacteniDodavatele(int verzeDodavatele, IndexObjednavekDodavatele dodavatele)
            {
                _dodavatele = dodavatele;
                ExecuteInternal();
                _parent._cacheObjednavek.Insert(_cisloObjednavky, _verzeObjednavky, _objednavka, dirty: true);
                _onCompleted();
            }

            private void ExecuteInternal()
            {
                if (_objednavka == null)
                {
                    _objednavka = new IndexObjednavekDataObjednavek();
                    _objednavka.CisloObjednavky = _cisloObjednavky;
                }
                var existujici = _objednavka.Kandidati.FirstOrDefault(k => k.KodDodavatele == _kodDodavatele);
                if (existujici == null)
                {
                    existujici = new NalezenaObjednavka();
                    existujici.Objednavka = _cisloObjednavky;
                    existujici.KodDodavatele = _kodDodavatele;
                }
                IndexObjednavekDodavatel dodavatel;
                if (_dodavatele.IndexDodavatelu.TryGetValue(_kodDodavatele, out dodavatel))
                    existujici.NazevDodavatele = dodavatel.Nazev;
                existujici.TerminDodani = _terminDodani;
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new ZpracovatDodaciList(this, message.OnCompleted, message.OnError, message.Command.DodaciList, message.Command.KodDodavatele, message.Command.Objednavka).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new ZpracovatDodaciList(this, message.OnCompleted, message.OnError, message.Command.DodaciList, message.Command.KodDodavatele, message.Command.Objednavka).Execute();
        }

        private class ZpracovatDodaciList
        {
            private IndexObjednavekProjection _parent;
            private Action _onCompleted;
            private Action<Exception> _onError;
            private string _cisloDodacihoListu;
            private string _kodDodavatele;
            private string _cisloObjednavky;

            private int _verzeDodListu;
            private IndexObjednavekDataDodacichListu _dodaciListy;
            private IndexObjednavekDodavatele _dodavatele;

            public ZpracovatDodaciList(IndexObjednavekProjection parent, Action onCompleted, Action<Exception> onError, string dodaciList, string kodDodavatele, string cisloObjednavky)
            {
                _parent = parent;
                _onCompleted = onCompleted;
                _onError = onError;
                _cisloDodacihoListu = dodaciList;
                _kodDodavatele = kodDodavatele;
                _cisloObjednavky = cisloObjednavky;
            }

            public void Execute()
            {
                _parent._cacheDodacichListu.Get(
                    _cisloDodacihoListu,
                    (verze, objednavka) => NactenDodaciList(verze, objednavka),
                    ex => _onError(ex),
                    load => _parent._repository.NacistDodaciList(_cisloDodacihoListu, load.OldVersion, load.SetLoadedValue, load.ValueIsStillValid, load.LoadingFailed)
                    );
            }

            private void NactenDodaciList(int verzeDodListu, IndexObjednavekDataDodacichListu dodaciListy)
            {
                _verzeDodListu = verzeDodListu;
                _dodaciListy = dodaciListy;

                _parent._cacheDodavatelu.Get(
                    "dodavatele",
                    (verze, data) => NacteniDodavatele(verze, data),
                    ex => _onError(ex),
                    load => _parent._repository.NacistDodavatele(
                        (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                        load.LoadingFailed)
                    );
            }

            private void NacteniDodavatele(int verzeDodavatele, IndexObjednavekDodavatele dodavatele)
            {
                _dodavatele = dodavatele;
                ExecuteInternal();
                _parent._cacheDodacichListu.Insert(_cisloDodacihoListu, _verzeDodListu, _dodaciListy, dirty: true);
                _onCompleted();
            }

            private void ExecuteInternal()
            {
                if (_dodaciListy == null)
                {
                    _dodaciListy = new IndexObjednavekDataDodacichListu();
                    _dodaciListy.CisloDodacihoListu = _cisloDodacihoListu;
                }
                var existujici = _dodaciListy.Kandidati.FirstOrDefault(k => k.KodDodavatele == _kodDodavatele);
                if (existujici == null)
                {
                    existujici = new NalezenyDodaciList();
                    existujici.DodaciList = _cisloObjednavky;
                    existujici.KodDodavatele = _kodDodavatele;
                }
                existujici.Objednavky = existujici.Objednavky ?? new List<string>();
                IndexObjednavekDodavatel dodavatel;
                if (_dodavatele.IndexDodavatelu.TryGetValue(_kodDodavatele, out dodavatel))
                    existujici.NazevDodavatele = dodavatel.Nazev;
                if (!existujici.Objednavky.Contains(_cisloObjednavky))
                    existujici.Objednavky.Add(_cisloObjednavky);
            }
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

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            _cacheDodavatelu.Get(
                "dodavatele",
                (verze, data) =>
                {
                    IndexObjednavekDodavatel existujici;
                    if (!data.IndexDodavatelu.TryGetValue(message.Command.Kod, out existujici))
                    {
                        existujici = new IndexObjednavekDodavatel();
                        existujici.Kod = message.Command.Kod;
                        data.Dodavatele.Add(existujici);
                        data.IndexDodavatelu[existujici.Kod] = existujici;
                    }
                    existujici.Nazev = message.Command.Nazev;
                    _cacheDodavatelu.Insert("dodavatele", verze, data, dirty: true);
                    message.OnCompleted();
                },
                ex => message.OnError(ex),
                load => _repository.NacistDodavatele((verze, data) => load.SetLoadedValue(verze, RozsiritData(data)), load.LoadingFailed)
                );
        }
    }

    public class IndexObjednavekRepository
    {
        private IDocumentFolder _folder;

        public IndexObjednavekRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void NacistDodavatele(Action<int, IndexObjednavekDodavatele> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("dodavatele",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<IndexObjednavekDodavatele>(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitDodavatele(int verze, IndexObjednavekDodavatele data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "dodavatele",
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistObjednavku(string cisloObjednavky, int znamaVerze, Action<int, IndexObjednavekDataObjednavek> onLoaded, Action onValid, Action<Exception> onError)
        {
            _folder.GetNewerDocument(NazevDokumentuObjednavky(cisloObjednavky), znamaVerze,
                (verze, raw) => onLoaded(verze, JsonSerializer.DeserializeFromString<IndexObjednavekDataObjednavek>(raw)),
                () => onValid(), () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitObjednavku(int verze, IndexObjednavekDataObjednavek data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                NazevDokumentuObjednavky(data.CisloObjednavky),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistDodaciList(string cisloDodacihoListu, int znamaVerze, Action<int, IndexObjednavekDataDodacichListu> onLoaded, Action onValid, Action<Exception> onError)
        {
            _folder.GetNewerDocument(NazevDokumentuDodacihoListu(cisloDodacihoListu), znamaVerze,
                (verze, raw) => onLoaded(verze, JsonSerializer.DeserializeFromString<IndexObjednavekDataDodacichListu>(raw)),
                () => onValid(), () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitDodaciList(int verze, IndexObjednavekDataDodacichListu data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                NazevDokumentuDodacihoListu(data.CisloDodacihoListu),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        private static string NazevDokumentuObjednavky(string cislo)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", cislo);
        }

        private static string NazevDokumentuDodacihoListu(string cislo)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("dodacilist-", cislo);
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
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

        public IndexObjednavekReader(IndexObjednavekRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheObjednavek = new MemoryCache<NajitObjednavkuResponse>(executor, time);
            _cacheDodacichListu = new MemoryCache<NajitDodaciListResponse>(executor, time);
        }

        public void Handle(QueryExecution<NajitObjednavkuRequest, NajitObjednavkuResponse> message)
        {
            _cacheObjednavek.Get(message.Request.Objednavka, (verze, data) => message.OnCompleted(data), message.OnError,
                load => _repository.NacistObjednavku(message.Request.Objednavka, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponseObjednavky(message.Request.Objednavka, data)),
                    load.ValueIsStillValid, load.LoadingFailed));
        }

        private NajitObjednavkuResponse VytvoritResponseObjednavky(string cisloObjednavky, IndexObjednavekDataObjednavek zaklad)
        {
            return new NajitObjednavkuResponse
            {
                Objednavka = cisloObjednavky,
                Nalezena = zaklad != null,
                Kandidati = zaklad.Kandidati
            };
        }

        public void Handle(QueryExecution<NajitDodaciListRequest, NajitDodaciListResponse> message)
        {
            _cacheDodacichListu.Get(message.Request.DodaciList, (verze, data) => message.OnCompleted(data), message.OnError,
                load => _repository.NacistDodaciList(message.Request.DodaciList, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponseDodacihoListu(message.Request.DodaciList, data)),
                    load.ValueIsStillValid, load.LoadingFailed));
        }

        private NajitDodaciListResponse VytvoritResponseDodacihoListu(string dodaciList, IndexObjednavekDataDodacichListu zaklad)
        {
            return new NajitDodaciListResponse
            {
                DodaciList = dodaciList,
                Nalezen = zaklad != null,
                Kandidati = zaklad.Kandidati
            };
        }
    }
}
