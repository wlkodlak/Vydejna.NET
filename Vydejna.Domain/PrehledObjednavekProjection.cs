using ServiceLib;
using ServiceStack.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class PrehledObjednavekProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<CislovaneNaradiPrijatoZOpravyEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPredanoKOpraveEvent>>
        , IHandle<CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent>>
    {
        private IDocumentFolder _store;
        private PrehledObjednavekSerializer _serializer;
        private PrehledObjednavekDataCiselniku _ciselnikyData;
        private PrehledObjednavekDataObjednavek _objednavkyData;
        private int _ciselnikyVerze, _objednavkyVerze;
        private bool _ciselnikyDirty, _objednavkyDirty;
        private bool _dokoncenFlush;

        public PrehledObjednavekProjection(IDocumentFolder store)
        {
            _store = store;
            _serializer = new PrehledObjednavekSerializer();
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
            _ciselnikyData = _serializer.NacistCiselniky(null);
            _objednavkyData = _serializer.NacistObjednavkyKompletne(null);
            _ciselnikyVerze = _objednavkyVerze = 0;
            _ciselnikyDirty = _objednavkyDirty = false;
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            new ResumeWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class ResumeWorker
        {
            private PrehledObjednavekProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public ResumeWorker(PrehledObjednavekProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                _parent._store.GetDocument("ciselniky", NacistCiselniky, () => NacistCiselniky(0, null), _onError);
            }

            private void NacistCiselniky(int verze, string raw)
            {
                _parent._ciselnikyData = _parent._serializer.NacistCiselniky(raw);
                _parent._ciselnikyDirty = false;
                _parent._ciselnikyVerze = verze;

                _parent._store.GetDocument("objednavky", NacistObjednavky, () => NacistObjednavky(0, null), _onError);
            }

            private void NacistObjednavky(int verze, string raw)
            {
                _parent._objednavkyData = _parent._serializer.NacistObjednavkyKompletne(raw);
                _parent._objednavkyDirty = false;
                _parent._objednavkyVerze = verze;

                _onComplete();
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private PrehledObjednavekProjection _parent;
            private Action _onComplete;
            private Action<Exception> _onError;

            public FlushWorker(PrehledObjednavekProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute()
            {
                if (_parent._ciselnikyDirty)
                {
                    _parent._store.SaveDocument(
                        "ciselniky",
                        _parent._serializer.UlozitCiselniky(_parent._ciselnikyData),
                        DocumentStoreVersion.At(_parent._ciselnikyVerze), null,
                        () => { _parent._ciselnikyVerze++; UlozitObjednavky(); },
                        Concurrency, _onError);
                }
                else
                    UlozitObjednavky();
            }

            private void UlozitObjednavky()
            {
                if (_parent._objednavkyDirty)
                {
                    var data = _parent._objednavkyData;
                    if (data.PocetZpravPredFlushem > 0)
                    {
                        data.ZpracovaneZpravy.RemoveRange(0, data.PocetZpravPredFlushem);
                        data.IndexZprav = new HashSet<Guid>(data.ZpracovaneZpravy);
                        data.PocetZpravPredFlushem = 0;
                    }
                    _parent._store.SaveDocument(
                        "objednavky",
                        _parent._serializer.UlozitObjednavky(_parent._objednavkyData),
                        DocumentStoreVersion.At(_parent._objednavkyVerze), null,
                        () => { 
                            _parent._objednavkyVerze++;
                            _parent._dokoncenFlush = true;
                            _onComplete(); },
                        Concurrency, _onError);
                }
                else
                    _onComplete();
            }

            private void Concurrency()
            {
                _onError(new ProjectorMessages.ConcurrencyException());
            }
        }

        private void EliminovatZbytecneZpravy()
        {
            if (!_dokoncenFlush)
                return;
            _dokoncenFlush = false;
            _objednavkyData.PocetZpravPredFlushem = _objednavkyData.ZpracovaneZpravy.Count;
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            EliminovatZbytecneZpravy();
            PrehledObjednavekDataDodavatele dodavatel;
            if (!_ciselnikyData.IndexDodavatelu.TryGetValue(message.Command.Kod, out dodavatel))
            {
                dodavatel = new PrehledObjednavekDataDodavatele();
                dodavatel.KodDodavatele = message.Command.Kod;
                _ciselnikyData.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
                _ciselnikyData.SeznamDodavatelu.Add(dodavatel);
            }
            dodavatel.NazevDodavatele = message.Command.Nazev;
            _ciselnikyDirty = true;
            foreach (var objednavka in _objednavkyData.SeznamObjednavek)
            {
                if (objednavka.KodDodavatele == dodavatel.KodDodavatele)
                {
                    objednavka.NazevDodavatele = dodavatel.NazevDodavatele;
                    _objednavkyDirty = true;
                }
            }
        }

        private void UpravitObjednavku(Action onComplete, Action<Exception> onError, string kodDodavatele, string cisloObjednavky, Guid eventId, DateTime? termin, Action<ObjednavkaVPrehledu> zmena)
        {
            EliminovatZbytecneZpravy();
            if (_objednavkyData.IndexZprav.Contains(eventId))
            {
                onComplete();
                return;
            }
            _objednavkyDirty = true;
            _objednavkyData.IndexZprav.Add(eventId);
            _objednavkyData.ZpracovaneZpravy.Add(eventId);

            ObjednavkaVPrehledu objednavka;
            var klic = Tuple.Create(kodDodavatele, cisloObjednavky);
            if (!_objednavkyData.IndexObjednavek.TryGetValue(klic, out objednavka))
            {
                objednavka = new ObjednavkaVPrehledu();
                objednavka.KodDodavatele = kodDodavatele;
                objednavka.Objednavka = cisloObjednavky;
                _objednavkyData.IndexObjednavek[klic] = objednavka;
                _objednavkyData.SeznamObjednavek.Add(objednavka);
            }

            PrehledObjednavekDataDodavatele dodavatel;
            if (_ciselnikyData.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel))
                objednavka.NazevDodavatele = dodavatel.NazevDodavatele;

            if (termin.HasValue)
                objednavka.TerminDodani = termin.Value;

            zmena(objednavka);
            onComplete();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitObjednavku(message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka, message.Command.EventId, message.Command.TerminDodani,
                obj => { obj.PocetObjednanych += 1; });
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitObjednavku(message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka, message.Command.EventId, null,
                obj =>
                {
                    if (message.Command.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += 1;
                    else
                        obj.PocetOpravenych += 1;
                });
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            UpravitObjednavku(message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka, message.Command.EventId, message.Command.TerminDodani,
                obj => { obj.PocetObjednanych += message.Command.Pocet; });
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            UpravitObjednavku(message.OnCompleted, message.OnError, message.Command.KodDodavatele, message.Command.Objednavka, message.Command.EventId, null,
                obj =>
                {
                    if (message.Command.Opraveno == StavNaradiPoOprave.Neopravitelne)
                        obj.PocetNeopravitelnych += message.Command.Pocet;
                    else
                        obj.PocetOpravenych += message.Command.Pocet;
                });
        }
    }

    public class PrehledObjednavekDataObjednavek
    {
        public List<ObjednavkaVPrehledu> SeznamObjednavek { get; set; }
        public Dictionary<Tuple<string, string>, ObjednavkaVPrehledu> IndexObjednavek;
        public List<Guid> ZpracovaneZpravy { get; set; }
        public HashSet<Guid> IndexZprav;
        public int PocetZpravPredFlushem;
    }
    public class PrehledObjednavekDataCiselniku
    {
        public List<PrehledObjednavekDataDodavatele> SeznamDodavatelu { get; set; }
        public Dictionary<string, PrehledObjednavekDataDodavatele> IndexDodavatelu;
    }
    public class PrehledObjednavekDataDodavatele
    {
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
    }

    public class PrehledObjednavekSerializer
    {
        public PrehledObjednavekDataObjednavek NacistObjednavkyProReader(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledObjednavekDataObjednavek>(raw);
            result = result ?? new PrehledObjednavekDataObjednavek();
            result.SeznamObjednavek = result.SeznamObjednavek ?? new List<ObjednavkaVPrehledu>();
            return result;
        }

        public PrehledObjednavekDataObjednavek NacistObjednavkyKompletne(string raw)
        {
            var result = NacistObjednavkyProReader(raw);
            result.IndexObjednavek = new Dictionary<Tuple<string, string>, ObjednavkaVPrehledu>(result.SeznamObjednavek.Count);
            foreach (var objednavka in result.SeznamObjednavek)
                result.IndexObjednavek[Tuple.Create(objednavka.KodDodavatele, objednavka.Objednavka)] = objednavka;
            result.ZpracovaneZpravy = new List<Guid>();
            result.IndexZprav = new HashSet<Guid>(result.ZpracovaneZpravy);
            result.PocetZpravPredFlushem = 0;
            return result;
        }

        public string UlozitObjednavky(PrehledObjednavekDataObjednavek data)
        {
            return JsonSerializer.SerializeToString(data);
        }

        public PrehledObjednavekDataCiselniku NacistCiselniky(string raw)
        {
            var result = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<PrehledObjednavekDataCiselniku>(raw);
            result = result ?? new PrehledObjednavekDataCiselniku();
            result.SeznamDodavatelu = result.SeznamDodavatelu ?? new List<PrehledObjednavekDataDodavatele>();
            result.IndexDodavatelu = new Dictionary<string, PrehledObjednavekDataDodavatele>(result.SeznamDodavatelu.Count);
            foreach (var dodavatel in result.SeznamDodavatelu)
                result.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
            return result;
        }

        public string UlozitCiselniky(PrehledObjednavekDataCiselniku data)
        {
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class PrehledObjednavekReader
        : IAnswer<PrehledObjednavekRequest, PrehledObjednavekResponse>
    {
        private IDocumentFolder _store;
        private MemoryCache<PrehledObjednavekResponse> _cache;
        private PrehledObjednavekSerializer _serializer;

        public PrehledObjednavekReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<PrehledObjednavekResponse>(executor, time);
            _serializer = new PrehledObjednavekSerializer();
        }

        public void Handle(QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse> message)
        {
            _cache.Get("objednavky", (verze, data) => message.OnCompleted(data), message.OnError, NacistObjednavky);
        }

        private void NacistObjednavky(IMemoryCacheLoad<PrehledObjednavekResponse> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(load.Key, load.OldVersion,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument(load.Key,
                    (verze, raw) => load.SetLoadedValue(verze, VytvoritResponse(raw)),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    load.LoadingFailed);
            }
        }

        private PrehledObjednavekResponse VytvoritResponse(string raw)
        {
            var zaklad = _serializer.NacistObjednavkyProReader(raw);
            var response = new PrehledObjednavekResponse();
            response.Seznam = zaklad.SeznamObjednavek;
            return response;
        }
    }
}
