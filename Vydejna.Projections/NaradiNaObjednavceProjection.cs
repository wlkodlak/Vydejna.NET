using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Projections.NaradiNaObjednavceReadModel
{
    public class NaradiNaObjednavceProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
    {
        private NaradiNaObjednavceRepository _repository;
        private MemoryCache<NaradiNaObjednavceDataObjednavky> _cacheObjednavek;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<NaradiNaObjednavceDataDodavatele> _cacheDodavatele;

        public NaradiNaObjednavceProjection(NaradiNaObjednavceRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheObjednavek = new MemoryCache<NaradiNaObjednavceDataObjednavky>(executor, time);
            _cacheNaradi = new MemoryCache<InformaceONaradi>(executor, time);
            _cacheDodavatele = new MemoryCache<NaradiNaObjednavceDataDodavatele>(executor, time);
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
            _cacheDodavatele.Clear();
            _cacheNaradi.Clear();
            _cacheObjednavek.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
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
                _parent._cacheDodavatele.Flush(UlozitNaradi, _onError, save => _parent._repository.UlozitDodavatele(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void UlozitNaradi()
            {
                _parent._cacheNaradi.Flush(UlozitObjednavky, _onError, save => _parent._repository.UlozitNaradi(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }

            private void UlozitObjednavky()
            {
                _parent._cacheObjednavek.Flush(_onComplete, _onError, save => _parent._repository.UlozitObjednavku(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            var klic = NaradiNaObjednavceRepository.DokumentNaradi(message.Command.NaradiId);
            _cacheNaradi.Get(
                klic,
                (verze, naradiInfo) => ZpracovatDefiniciNaradi(message, klic, verze, naradiInfo),
                message.OnError,
                load => _repository.NacistNaradi(message.Command.NaradiId, load.SetLoadedValue, load.LoadingFailed));
        }

        private void ZpracovatDefiniciNaradi(CommandExecution<DefinovanoNaradiEvent> message, string klic, int verze, InformaceONaradi naradiInfo)
        {
            if (naradiInfo == null)
            {
                naradiInfo = new InformaceONaradi();
                naradiInfo.NaradiId = message.Command.NaradiId;
            }
            naradiInfo.Vykres = message.Command.Vykres;
            naradiInfo.Rozmer = message.Command.Rozmer;
            naradiInfo.Druh = message.Command.Druh;
            _cacheNaradi.Insert(klic, verze, naradiInfo, dirty: true);
            message.OnCompleted();
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            _cacheDodavatele.Get(
                "dodavatele",
                (verze, dodavatele) => ZpracovatDefiniciDodavatele(message, verze, dodavatele),
                message.OnError,
                load => _repository.NacistDodavatele((verze, data) => load.SetLoadedValue(verze, RozsiritData(data)), load.LoadingFailed));
        }

        private static NaradiNaObjednavceDataDodavatele RozsiritData(NaradiNaObjednavceDataDodavatele data)
        {
            if (data == null)
            {
                data = new NaradiNaObjednavceDataDodavatele();
                data.SeznamDodavatelu = new List<InformaceODodavateli>();
                data.IndexDodavatelu = new Dictionary<string, InformaceODodavateli>();
            }
            else if (data.IndexDodavatelu == null)
            {
                data.IndexDodavatelu = new Dictionary<string, InformaceODodavateli>();
                foreach (var dodavatel in data.SeznamDodavatelu)
                    data.IndexDodavatelu[dodavatel.Kod] = dodavatel;
            }
            return data;
        }

        private void ZpracovatDefiniciDodavatele(CommandExecution<DefinovanDodavatelEvent> message, int verze, NaradiNaObjednavceDataDodavatele dodavatele)
        {
            InformaceODodavateli dodavatelInfo;
            if (!dodavatele.IndexDodavatelu.TryGetValue(message.Command.Kod, out dodavatelInfo))
            {
                dodavatelInfo = new InformaceODodavateli();
                dodavatelInfo.Kod = message.Command.Kod;
                dodavatele.SeznamDodavatelu.Add(dodavatelInfo);
                dodavatele.IndexDodavatelu[dodavatelInfo.Kod] = dodavatelInfo;
            }
            dodavatelInfo.Nazev = message.Command.Nazev;
            dodavatelInfo.Adresa = message.Command.Adresa;
            dodavatelInfo.Ico = message.Command.Ico;
            dodavatelInfo.Dic = message.Command.Dic;
            dodavatelInfo.Aktivni = !message.Command.Deaktivovan;
            _cacheDodavatele.Insert("dodavatele", verze, dodavatele, dirty: true);
            message.OnCompleted();
        }

        private class UpravitObjednavku
        {
            private NaradiNaObjednavceProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;
            private string _kodDodavatele;
            private string _cisloObjednavky;
            private Guid _naradiId;
            private int _cisloNaradi;
            private int _novyPocet;
            private DateTime? _terminDodani;

            private int _verzeObjednavky;
            private NaradiNaObjednavceDataObjednavky _dataObjednavky;
            private InformaceONaradi _naradiInfo;
            private InformaceODodavateli _dodavatel;
            private string _nazevDokumentuObjednavky;

            public UpravitObjednavku(NaradiNaObjednavceProjection parent, string kodDodavatele, string cisloObjednavky, Action onComplete, Action<Exception> onError,
                Guid naradiId, int cisloNaradi, int novyPocet, DateTime? terminDodani)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
                _kodDodavatele = kodDodavatele;
                _cisloObjednavky = cisloObjednavky;
                _naradiId = naradiId;
                _cisloNaradi = cisloNaradi;
                _novyPocet = novyPocet;
                _terminDodani = terminDodani;
            }

            public void Execute()
            {
                _nazevDokumentuObjednavky = NaradiNaObjednavceRepository.DokumentObjednavky(_kodDodavatele, _cisloObjednavky);
                _parent._cacheObjednavek.Get(
                    _nazevDokumentuObjednavky,
                    (verze, data) => NactenyObjednavky(verze, data),
                    ex => _onError(ex),
                    load => _parent._repository.NacistObjednavku(_kodDodavatele, _cisloObjednavky, load.SetLoadedValue, load.LoadingFailed));

            }

            private void NactenyObjednavky(int verzeObjednavky, NaradiNaObjednavceDataObjednavky dataObjednavky)
            {
                _verzeObjednavky = verzeObjednavky;
                _dataObjednavky = dataObjednavky;

                _parent._cacheNaradi.Get(
                    NaradiNaObjednavceRepository.DokumentNaradi(_naradiId),
                    (verze, data) => NactenoNaradi(verze, data),
                    ex => _onError(ex),
                    load => _parent._repository.NacistNaradi(_naradiId, load.SetLoadedValue, load.LoadingFailed)
                    );
            }

            private void NactenoNaradi(int verzeNaradi, InformaceONaradi dataNaradi)
            {
                _naradiInfo = dataNaradi;

                _parent._cacheDodavatele.Get(
                    "dodavatele",
                    (verze, data) => NacteniDodavatele(verze, data),
                    ex => _onError(ex),
                    load => _parent._repository.NacistDodavatele(load.SetLoadedValue, load.LoadingFailed)
                    );
            }

            private void NacteniDodavatele(int verze, NaradiNaObjednavceDataDodavatele data)
            {
                if (!data.IndexDodavatelu.TryGetValue(_kodDodavatele, out _dodavatel))
                {
                    _dodavatel = new InformaceODodavateli();
                    _dodavatel.Kod = _kodDodavatele;
                }

                ExecuteInternal();
                _parent._cacheObjednavek.Insert(_nazevDokumentuObjednavky, _verzeObjednavky, _dataObjednavky, dirty: true);
                _onComplete();
            }

            private void ExecuteInternal()
            {
                NaradiNaObjednavce naradiNaObjednavce;

                if (_dataObjednavky == null)
                {
                    _dataObjednavky = new NaradiNaObjednavceDataObjednavky();
                    _dataObjednavky.Seznam = new List<NaradiNaObjednavce>();
                    _dataObjednavky.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaObjednavce>();
                }
                else if (_dataObjednavky.IndexPodleIdNaradi == null)
                {
                    _dataObjednavky.IndexPodleIdNaradi = new Dictionary<Guid, NaradiNaObjednavce>();
                    foreach (var naradi in _dataObjednavky.Seznam)
                        _dataObjednavky.IndexPodleIdNaradi[naradi.NaradiId] = naradi;
                }

                _dataObjednavky.Objednavka = _cisloObjednavky;
                _dataObjednavky.Dodavatel = _dodavatel;
                if (_terminDodani != null)
                    _dataObjednavky.TerminDodani = _terminDodani;

                if (!_dataObjednavky.IndexPodleIdNaradi.TryGetValue(_naradiId, out naradiNaObjednavce))
                {
                    naradiNaObjednavce = new NaradiNaObjednavce();
                    naradiNaObjednavce.NaradiId = _naradiId;
                    naradiNaObjednavce.SeznamCislovanych = new List<int>();
                    _dataObjednavky.IndexPodleIdNaradi[_naradiId] = naradiNaObjednavce;
                    _dataObjednavky.Seznam.Add(naradiNaObjednavce);
                }
                if (_naradiInfo != null)
                {
                    naradiNaObjednavce.Vykres = _naradiInfo.Vykres;
                    naradiNaObjednavce.Rozmer = _naradiInfo.Rozmer;
                    naradiNaObjednavce.Druh = _naradiInfo.Druh;
                }

                if (_cisloNaradi == 0)
                {
                    naradiNaObjednavce.PocetNecislovanych = _novyPocet;
                }
                else
                {
                    if (_novyPocet == 1 && !naradiNaObjednavce.SeznamCislovanych.Contains(_cisloNaradi))
                        naradiNaObjednavce.SeznamCislovanych.Add(_cisloNaradi);
                    else if (_novyPocet == 0)
                        naradiNaObjednavce.SeznamCislovanych.Remove(_cisloNaradi);
                }
                naradiNaObjednavce.PocetCelkem = naradiNaObjednavce.PocetNecislovanych + naradiNaObjednavce.SeznamCislovanych.Count;
                _dataObjednavky.PocetCelkem = _dataObjednavky.IndexPodleIdNaradi.Values.Sum(n => n.PocetCelkem);

                if (naradiNaObjednavce.PocetCelkem == 0)
                {
                    _dataObjednavky.Seznam.Remove(naradiNaObjednavce);
                    _dataObjednavky.IndexPodleIdNaradi.Remove(_naradiId);
                }
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            new UpravitObjednavku(this, message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, message.Command.CisloNaradi, 1, message.Command.TerminDodani).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new UpravitObjednavku(this, message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, message.Command.CisloNaradi, 0, null).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            new UpravitObjednavku(this, message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, 0, message.Command.PocetNaNovem, message.Command.TerminDodani).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            new UpravitObjednavku(this, message.Command.KodDodavatele, message.Command.Objednavka, message.OnCompleted, message.OnError,
                message.Command.NaradiId, 0, message.Command.PocetNaPredchozim, null).Execute();
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
    public class NaradiNaObjednavceDataDodavatele
    {
        public List<InformaceODodavateli> SeznamDodavatelu { get; set; }
        public Dictionary<string, InformaceODodavateli> IndexDodavatelu;
    }

    public class NaradiNaObjednavceRepository
    {
        private IDocumentFolder _folder;

        public NaradiNaObjednavceRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void NacistDodavatele(Action<int, NaradiNaObjednavceDataDodavatele> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("dodavatele",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataDodavatele>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitDodavatele(int verze, NaradiNaObjednavceDataDodavatele dodavatele, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "dodavatele",
                JsonSerializer.SerializeToString(dodavatele),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public static string DokumentObjednavky(string kodDodavatele, string cisloObjednavky)
        {
            return DocumentStoreUtils.CreateBasicDocumentName("objednavka-", kodDodavatele, "-", cisloObjednavky);
        }

        public void NacistObjednavku(string kodDodavatele, string cisloObjednavky, Action<int, NaradiNaObjednavceDataObjednavky> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                DokumentObjednavky(kodDodavatele, cisloObjednavky),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<NaradiNaObjednavceDataObjednavky>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitObjednavku(int verze, NaradiNaObjednavceDataObjednavky objednavka, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                DokumentObjednavky(objednavka.Dodavatel.Kod, objednavka.Objednavka),
                JsonSerializer.SerializeToString(objednavka),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public static string DokumentNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public void NacistNaradi(Guid naradiId, Action<int, InformaceONaradi> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                DokumentNaradi(naradiId),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceONaradi>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitNaradi(int verze, InformaceONaradi naradi, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                DokumentNaradi(naradi.NaradiId),
                JsonSerializer.SerializeToString(naradi),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }
    }

    public class NaradiNaObjednavceReader
       : IAnswer<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>
    {
        private MemoryCache<ZiskatNaradiNaObjednavceResponse> _cache;
        private NaradiNaObjednavceRepository _repository;

        public NaradiNaObjednavceReader(NaradiNaObjednavceRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cache = new MemoryCache<ZiskatNaradiNaObjednavceResponse>(executor, time);
        }

        public void Handle(QueryExecution<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse> message)
        {
            _cache.Get(
                NaradiNaObjednavceRepository.DokumentObjednavky(message.Request.KodDodavatele, message.Request.Objednavka),
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load => _repository.NacistObjednavku(message.Request.KodDodavatele, message.Request.Objednavka,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponse(message.Request.KodDodavatele, message.Request.Objednavka, data)),
                    load.LoadingFailed));
        }

        private ZiskatNaradiNaObjednavceResponse VytvoritResponse(string kodDodavatele, string cisloObjednavky, NaradiNaObjednavceDataObjednavky zaklad)
        {
            if (zaklad == null)
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
            else
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
        }
    }
}
