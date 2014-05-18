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
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private PrehledCislovanehoNaradiProjection _parent;
            private Action _onCompleted;
            private Action<Exception> _onError;

            public FlushWorker(PrehledCislovanehoNaradiProjection parent, Action onComplete, Action<Exception> onError)
            {
                this._parent = parent;
                this._onCompleted = onComplete;
                this._onError = onError;
            }

            public void Execute()
            {
                _parent._cacheNaradi.Flush(UlozitCislovane, _onError, save => _parent._repository.UlozitNaradi(save.Version, save.Value));
            }
            private void UlozitCislovane()
            {
                _parent._cacheCislovane.Flush(_onCompleted, _onError, save => _parent._repository.UlozitCislovane(save.Version, save.Value, ZobrazitVPrehledu(save.Value)));
            }
            private static bool ZobrazitVPrehledu(CislovaneNaradiVPrehledu data)
            {
                return data.Umisteni != null && data.Umisteni.ZakladniUmisteni != ZakladUmisteni.VeSrotu;
            }
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            _cacheNaradi.Get(
                DokumentNaradi(message.NaradiId),
                (verze, naradi) =>
                {
                    if (naradi == null)
                    {
                        naradi = new InformaceONaradi();
                        naradi.NaradiId = message.NaradiId;
                    }
                    naradi.Vykres = message.Vykres;
                    naradi.Rozmer = message.Rozmer;
                    naradi.Druh = message.Druh;

                    _cacheNaradi.Insert(DokumentNaradi(message.NaradiId), verze, naradi, dirty: true);
                    message.OnCompleted();
                },
                ex => message.OnError(ex),
                load => _repository.NacistNaradi(message.NaradiId, load.SetLoadedValue, load.LoadingFailed)
                );
        }

        private class PresunNaradi
        {
            private PrehledCislovanehoNaradiProjection _parent;
            private Guid _naradiId;
            private int _cisloNaradi;
            private UmisteniNaradiDto _umisteni;
            private decimal _cena;
            private Action _onCompleted;
            private Action<Exception> _onError;

            private InformaceONaradi _naradiInfo;
            private string _dokumentCislovane;
            private int _verzeCislovane;
            private CislovaneNaradiVPrehledu _dataCislovane;

            public PresunNaradi(PrehledCislovanehoNaradiProjection parent, Guid naradiId, int cisloNaradi, UmisteniNaradiDto umisteni, decimal cena, Action onCompleted, Action<Exception> onError)
            {
                _parent = parent;
                _naradiId = naradiId;
                _cisloNaradi = cisloNaradi;
                _umisteni = umisteni;
                _cena = cena;
                _onCompleted = onCompleted;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._cacheNaradi.Get(
                    DokumentNaradi(_naradiId), NactenoNaradi, _onError,
                    load => _parent._repository.NacistNaradi(_naradiId, load.SetLoadedValue, load.LoadingFailed)
                    );
            }

            private void NactenoNaradi(int verze, InformaceONaradi naradiInfo)
            {
                _naradiInfo = naradiInfo;
                _dokumentCislovane = DokumentCislovane(_naradiId, _cisloNaradi);

                _parent._cacheCislovane.Get(
                    _dokumentCislovane, NactenoCislovane, _onError,
                    load => _parent._repository.NacistCislovane(_naradiId, _cisloNaradi, load.SetLoadedValue, load.LoadingFailed));
            }

            private void NactenoCislovane(int verzeCislovane, CislovaneNaradiVPrehledu dataCislovane)
            {
                _verzeCislovane = verzeCislovane;
                _dataCislovane = dataCislovane;

                ExecuteInternal();
                _parent._cacheCislovane.Insert(_dokumentCislovane, _verzeCislovane, _dataCislovane, dirty: true);
                _onCompleted();
            }

            private void ExecuteInternal()
            {
                if (_dataCislovane == null)
                {
                    _dataCislovane = new CislovaneNaradiVPrehledu();
                    _dataCislovane.NaradiId = _naradiId;
                    _dataCislovane.CisloNaradi = _cisloNaradi;
                }
                if (_naradiInfo != null)
                {
                    _dataCislovane.Vykres = _naradiInfo.Vykres;
                    _dataCislovane.Rozmer = _naradiInfo.Rozmer;
                    _dataCislovane.Druh = _naradiInfo.Druh;
                }
                _dataCislovane.Umisteni = _umisteni;
                _dataCislovane.Cena = _cena;
            }
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent message)
        {
            new PresunNaradi(this, message.NaradiId, message.CisloNaradi, message.NoveUmisteni, message.CenaNova, message.OnCompleted, message.OnError).Execute();
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

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistNaradi(Guid naradiId, Action<int, InformaceONaradi> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                PrehledCislovanehoNaradiProjection.DokumentNaradi(naradiId),
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<InformaceONaradi>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitNaradi(int verze, InformaceONaradi data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                PrehledCislovanehoNaradiProjection.DokumentNaradi(data.NaradiId),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistCislovane(Guid naradiId, int cisloNaradi, Action<int, CislovaneNaradiVPrehledu> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                PrehledCislovanehoNaradiProjection.DokumentCislovane(naradiId, cisloNaradi),
                (verze, raw) => onLoaded(verze, DeserializovatCislovane(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        private static CislovaneNaradiVPrehledu DeserializovatCislovane(string raw)
        {
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<CislovaneNaradiVPrehledu>(raw);
        }

        public void UlozitCislovane(int verze, CislovaneNaradiVPrehledu data, bool zobrazitVPrehledu, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                PrehledCislovanehoNaradiProjection.DokumentCislovane(data.NaradiId, data.CisloNaradi),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                zobrazitVPrehledu ? new[] { new DocumentIndexing("cisloNaradi", data.CisloNaradi.ToString("00000000")) } : null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistSeznamCislovanych(int offset, int pocet, Action<int, List<CislovaneNaradiVPrehledu>> onLoaded, Action<Exception> onError)
        {
            _folder.FindDocuments("cisloNaradi", null, null, offset, pocet, true,
                seznam => onLoaded(seznam.TotalFound, seznam.Select(c => DeserializovatCislovane(c.Contents)).ToList()),
                ex => onError(ex));
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
            _cache.Get(message.Request.Stranka.ToString(), (verze, response) => message.OnCompleted(response), message.OnError,
                load => _repository.NacistSeznamCislovanych(
                    message.Request.Stranka * 100 - 100, 100,
                    (celkem, seznam) => load.SetLoadedValue(1, VytvoritResponse(message.Request, celkem, seznam)),
                    load.LoadingFailed));
        }

        private PrehledCislovanehoNaradiResponse VytvoritResponse(PrehledCislovanehoNaradiRequest request, int celkem, List<CislovaneNaradiVPrehledu> seznam)
        {
            return new PrehledCislovanehoNaradiResponse
            {
                Stranka = request.Stranka,
                PocetCelkem = celkem,
                PocetStranek = (celkem + 99) / 100,
                Seznam = seznam
            };
        }
    }
}
