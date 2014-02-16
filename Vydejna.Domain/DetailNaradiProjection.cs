using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class DetailNaradiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.RebuildFinished>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<AktivovanoNaradiEvent>>
        , IHandle<CommandExecution<DeaktivovanoNaradiEvent>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
        , IHandle<CommandExecution<DefinovanaVadaNaradiEvent>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
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
        private const string _version = "0.01";
        private IDocumentFolder _store;
        private DetailNaradiSerializer _serializer;
        private DetailNaradiDataCiselniky _ciselnikyData;
        private bool _ciselnikyDirty;
        private int _ciselnikyVerze;
        private MemoryCache<DetailNaradiData> _naradiCache;

        public DetailNaradiProjection(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _serializer = new DetailNaradiSerializer();
            _ciselnikyData = null;
            _naradiCache = new MemoryCache<DetailNaradiData>(executor, time).SetupExpiration(60000, 60000, 1000);
        }

        public static string KlicIndexuOprav(string dodavatel, string objednavka)
        {
            return string.Concat(dodavatel, ":", objednavka);
        }

        public string GetVersion()
        {
            return _version;
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return string.Equals(storedVersion, _version, StringComparison.Ordinal) ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _ciselnikyData = _serializer.DeserializeCiselniky(null);
            _ciselnikyDirty = false;
            _ciselnikyVerze = 0;
            _naradiCache.Clear();
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.RebuildFinished> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            new FlushWorker(this, message.OnCompleted, message.OnError).Execute();
        }

        private class FlushWorker
        {
            private DetailNaradiProjection _parent;
            private Action _onCompleted;
            private Action<Exception> _onError;

            public FlushWorker(DetailNaradiProjection parent, Action onCompleted, Action<Exception> onError)
            {
                _parent = parent;
                _onCompleted = onCompleted;
                _onError = onError;
            }
            public void Execute()
            {
                if (_parent._ciselnikyDirty)
                {
                    _parent._ciselnikyDirty = false;
                    var serializovaneCiselniky = _parent._serializer.SerializeCiselniky(_parent._ciselnikyData);
                    _parent._store.SaveDocument("ciselniky", serializovaneCiselniky, DocumentStoreVersion.At(_parent._ciselnikyVerze), FlushNaradi, OnConcurrency, _onError);
                }
                else
                    FlushNaradi();
            }

            private void FlushNaradi()
            {
                _parent._naradiCache.Flush(_onCompleted, _onError, save =>
                {
                    var serialized = _parent._serializer.SerializeNaradi(save.Value);
                    _parent._store.SaveDocument(save.Key, serialized, DocumentStoreVersion.At(save.Version),
                        () => save.SavedAsVersion(save.Version + 1),
                        () => save.SavingFailed(new ProjectorMessages.ConcurrencyException()),
                        ex => save.SavingFailed(ex));
                });
            }

            private void OnConcurrency()
            {
                _onError(new ProjectorMessages.ConcurrencyException());
            }
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            Action<int, string> onFound = (version, raw) =>
            {
                _ciselnikyVerze = version;
                _ciselnikyDirty = false;
                _ciselnikyData = _serializer.DeserializeCiselniky(raw);
                message.OnCompleted();
            };
            _store.GetDocument("ciselniky", onFound, () => onFound(0, null), message.OnError);
        }

        private void ZpracovatNaradi(Guid naradiId, Action onCompleted, Action<Exception> onError, Action<DetailNaradiData> updateAction)
        {
            var nazevDokumentu = string.Concat("naradi-", naradiId.ToString("N"));
            _naradiCache.Get(
                nazevDokumentu,
                (verze, data) =>
                {
                    updateAction(data);
                    _naradiCache.Insert(nazevDokumentu, verze, data, dirty: true);
                    onCompleted();
                }, onError, NacistDataNaradi);
        }

        private void NacistDataNaradi(IMemoryCacheLoad<DetailNaradiData> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument(load.Key, load.OldVersion,
                    (version, raw) => load.SetLoadedValue(version, _serializer.DeserializeNaradi(raw)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, _serializer.DeserializeNaradi(null)),
                    ex => load.LoadingFailed(ex)
                    );
            }
            else
            {
                _store.GetDocument(load.Key,
                    (version, raw) => load.SetLoadedValue(version, _serializer.DeserializeNaradi(raw)),
                    () => load.SetLoadedValue(0, _serializer.DeserializeNaradi(null)),
                    ex => load.LoadingFailed(ex)
                    );
            }
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                data.NaradiId = evnt.NaradiId;
                data.Vykres = evnt.Vykres;
                data.Rozmer = evnt.Rozmer;
                data.Druh = evnt.Druh;
                data.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<AktivovanoNaradiEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                data.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<DeaktivovanoNaradiEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                data.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            DefinovanDodavatelEvent existujici;
            var novy = message.Command;
            bool zmeneno = false;
            if (!_ciselnikyData.IndexDodavatelu.TryGetValue(novy.Kod, out existujici))
            {
                zmeneno = true;
                _ciselnikyData.IndexDodavatelu[novy.Kod] = novy;
                _ciselnikyData.Dodavatele.Add(novy);
            }
            else
            {
                zmeneno =
                    existujici.Adresa != novy.Adresa ||
                    existujici.Deaktivovan != novy.Deaktivovan ||
                    existujici.Dic != novy.Dic ||
                    existujici.Ico != novy.Ico ||
                    existujici.Nazev != novy.Nazev;

                existujici.Adresa = novy.Adresa;
                existujici.Deaktivovan = novy.Deaktivovan;
                existujici.Dic = novy.Dic;
                existujici.Ico = novy.Ico;
                existujici.Nazev = novy.Nazev;
            }
            _ciselnikyDirty |= zmeneno;

            // tady by to chtelo vyvolat update vsech naradi
        }

        public void Handle(CommandExecution<DefinovanaVadaNaradiEvent> message)
        {
            DefinovanaVadaNaradiEvent existujici;
            var nova = message.Command;
            bool zmeneno = false;
            if (_ciselnikyData.IndexVad.TryGetValue(nova.Kod, out existujici))
            {
                zmeneno = true;
                _ciselnikyData.IndexVad[nova.Kod] = nova;
                _ciselnikyData.Vady.Add(nova);
            }
            else
            {
                zmeneno = existujici.Nazev != nova.Nazev;
                existujici.Nazev = nova.Nazev;
            }
            _ciselnikyDirty |= zmeneno;

            // tady by to chtelo vyvolat update vsech naradi
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            DefinovanoPracovisteEvent existujici;
            var nova = message.Command;
            bool zmeneno = false;
            if (_ciselnikyData.IndexPracovist.TryGetValue(nova.Kod, out existujici))
            {
                zmeneno = true;
                _ciselnikyData.IndexPracovist[nova.Kod] = nova;
                _ciselnikyData.Pracoviste.Add(nova);
            }
            else
            {
                zmeneno = existujici.Nazev != nova.Nazev || existujici.Stredisko != nova.Stredisko;
                existujici.Nazev = nova.Nazev;
                existujici.Stredisko = nova.Stredisko;
            }
            _ciselnikyDirty |= zmeneno;

            // tady by to chtelo vyvolat update vsech naradi
        }

        private StavNaradi StavNaradiPodleUmisteni(UmisteniNaradiDto umisteni)
        {
            if (umisteni.ZakladniUmisteni != ZakladUmisteni.NaVydejne)
                return StavNaradi.Neurcen;
            switch (umisteni.UpresneniZakladu)
            {
                case "VPoradku":
                    return StavNaradi.VPoradku;
                case "NutnoOpravit":
                    return StavNaradi.NutnoOpravit;
                case "Neopravitelne":
                    return StavNaradi.Neopravitelne;
                default:
                    return StavNaradi.Neurcen;
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                var cislovane = new DetailNaradiCislovane();
                cislovane.CisloNaradi = evnt.CisloNaradi;
                cislovane.Pocet = 1;
                cislovane.NaVydejne = new DetailNaradiNaVydejne();
                cislovane.NaVydejne.StavNaradi = StavNaradiPodleUmisteni(evnt.NoveUmisteni);
                data.Cislovane = null;
                data.IndexCislovane[evnt.CisloNaradi] = cislovane;
            });
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            throw new NotImplementedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            throw new NotImplementedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            throw new NotImplementedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            throw new NotImplementedException();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            throw new NotImplementedException();
        }
    }

    public class DetailNaradiData
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }
        public int NaSklade { get; set; }

        public DetailNaradiDataPocty PoctyCelkem { get; set; }
        public DetailNaradiDataPocty PoctyNecislovane { get; set; }
        public DetailNaradiDataPocty PoctyCislovane { get; set; }

        public List<DetailNaradiNecislovane> Necislovane { get; set; }
        public List<DetailNaradiCislovane> Cislovane { get; set; }
        public List<string> ReferenceDodavatelu { get; set; }
        public List<string> ReferenceVad { get; set; }
        public List<string> ReferencePracovist { get; set; }

        public Dictionary<int, DetailNaradiCislovane> IndexCislovane;
        public Dictionary<string, DetailNaradiNecislovane> IndexPodleObjednavky;
        public Dictionary<string, DetailNaradiNecislovane> IndexPodlePracoviste;
        public Dictionary<StavNaradi, DetailNaradiNecislovane> IndexPodleStavu;
        public HashSet<string> SetDodavatelu;
        public HashSet<string> SetVad;
        public HashSet<string> SetPracovist;
    }
    public class DetailNaradiDataCiselniky
    {
        public List<DefinovanaVadaNaradiEvent> Vady { get; set; }
        public List<DefinovanDodavatelEvent> Dodavatele { get; set; }
        public List<DefinovanoPracovisteEvent> Pracoviste { get; set; }
        public Dictionary<string, DefinovanaVadaNaradiEvent> IndexVad;
        public Dictionary<string, DefinovanDodavatelEvent> IndexDodavatelu;
        public Dictionary<string, DefinovanoPracovisteEvent> IndexPracovist;
    }

    public class DetailNaradiSerializer
    {
        public string SerializeNaradi(DetailNaradiData data)
        {
            data.ReferenceDodavatelu = data.ReferenceDodavatelu ?? data.SetDodavatelu.ToList();
            data.ReferencePracovist = data.ReferencePracovist ?? data.SetPracovist.ToList();
            data.ReferenceVad = data.ReferenceVad ?? data.SetVad.ToList();
            if (data.Cislovane == null)
                data.Cislovane = data.IndexCislovane.Values.ToList();
            if (data.Necislovane == null)
            {
                data.Necislovane = new List<DetailNaradiNecislovane>();
                data.Necislovane.AddRange(data.IndexPodleStavu.Values);
                data.Necislovane.AddRange(data.IndexPodlePracoviste.Values);
                data.Necislovane.AddRange(data.IndexPodleObjednavky.Values);
            }
            return JsonSerializer.SerializeToString(data);
        }
        public DetailNaradiData DeserializeNaradiForReader(string raw)
        {
            DetailNaradiData data;
            if (string.IsNullOrEmpty(raw))
                data = new DetailNaradiData();
            else
                data = JsonSerializer.DeserializeFromString<DetailNaradiData>(raw);

            data.PoctyCelkem = data.PoctyCelkem ?? new DetailNaradiDataPocty();
            data.PoctyCislovane = data.PoctyCislovane ?? new DetailNaradiDataPocty();
            data.PoctyNecislovane = data.PoctyNecislovane ?? new DetailNaradiDataPocty();
            data.Necislovane = data.Necislovane ?? new List<DetailNaradiNecislovane>();
            data.Cislovane = data.Cislovane ?? new List<DetailNaradiCislovane>();
            data.ReferenceDodavatelu = data.ReferenceDodavatelu ?? new List<string>();
            data.ReferencePracovist = data.ReferencePracovist ?? new List<string>();
            data.ReferenceVad = data.ReferenceVad ?? new List<string>();
            return data;
        }
        public DetailNaradiData DeserializeNaradi(string raw)
        {
            var data = DeserializeNaradiForReader(raw);
            data.IndexCislovane = data.Cislovane.ToDictionary(c => c.CisloNaradi);
            data.IndexPodleStavu = new Dictionary<StavNaradi, DetailNaradiNecislovane>();
            data.IndexPodlePracoviste = new Dictionary<string, DetailNaradiNecislovane>();
            data.IndexPodleObjednavky = new Dictionary<string, DetailNaradiNecislovane>();
            foreach (var naradi in data.Necislovane)
            {
                if (naradi.NaVydejne != null)
                    data.IndexPodleStavu[naradi.NaVydejne.StavNaradi] = naradi;
                if (naradi.VeVyrobe != null)
                    data.IndexPodlePracoviste[naradi.VeVyrobe.KodPracoviste] = naradi;
                if (naradi.VOprave != null)
                {
                    var klic = DetailNaradiProjection.KlicIndexuOprav(naradi.VOprave.KodDodavatele, naradi.VOprave.Objednavka);
                    data.IndexPodleObjednavky[klic] = naradi;
                }
            }
            data.SetDodavatelu = new HashSet<string>(data.ReferenceDodavatelu);
            data.SetPracovist = new HashSet<string>(data.ReferencePracovist);
            data.SetVad = new HashSet<string>(data.ReferenceVad);
            return data;
        }

        public string SerializeCiselniky(DetailNaradiDataCiselniky ciselniky)
        {
            return JsonSerializer.SerializeToString(ciselniky);
        }
        public DetailNaradiDataCiselniky DeserializeCiselniky(string raw)
        {
            DetailNaradiDataCiselniky ciselniky = null;
            if (!string.IsNullOrEmpty(raw))
                ciselniky = JsonSerializer.DeserializeFromString<DetailNaradiDataCiselniky>(raw);
            ciselniky = ciselniky ?? new DetailNaradiDataCiselniky();
            ciselniky.Pracoviste = ciselniky.Pracoviste ?? new List<DefinovanoPracovisteEvent>();
            ciselniky.Vady = ciselniky.Vady ?? new List<DefinovanaVadaNaradiEvent>();
            ciselniky.Dodavatele = ciselniky.Dodavatele ?? new List<DefinovanDodavatelEvent>();
            ciselniky.IndexDodavatelu = ciselniky.Dodavatele.ToDictionary(d => d.Kod);
            ciselniky.IndexPracovist = ciselniky.Pracoviste.ToDictionary(p => p.Kod);
            ciselniky.IndexVad = ciselniky.Vady.ToDictionary(v => v.Kod);
            return ciselniky;
        }
    }
}