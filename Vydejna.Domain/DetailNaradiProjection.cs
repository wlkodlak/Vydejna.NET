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
                    _parent._store.SaveDocument("ciselniky", serializovaneCiselniky, DocumentStoreVersion.At(_parent._ciselnikyVerze), null, FlushNaradi, OnConcurrency, _onError);
                }
                else
                    FlushNaradi();
            }

            private void FlushNaradi()
            {
                _parent._naradiCache.Flush(_onCompleted, _onError, save =>
                {
                    var serialized = _parent._serializer.SerializeNaradi(save.Value);
                    var indexy = _parent._serializer.IndexNaradi(save.Value);
                    _parent._store.SaveDocument(save.Key, serialized, DocumentStoreVersion.At(save.Version), indexy,
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
            DetailNaradiDataDodavatel existujici;
            var novy = message.Command;
            bool zmeneno = false;
            if (!_ciselnikyData.IndexDodavatelu.TryGetValue(novy.Kod, out existujici))
            {
                zmeneno = true;
                existujici = new DetailNaradiDataDodavatel();
                existujici.Kod = novy.Kod;
                existujici.Nazev = novy.Nazev;
                _ciselnikyData.IndexDodavatelu[novy.Kod] = existujici;
                _ciselnikyData.Dodavatele.Add(existujici);
            }
            else
            {
                zmeneno = existujici.Nazev != novy.Nazev;
                existujici.Nazev = novy.Nazev;
            }
            _ciselnikyDirty |= zmeneno;

            var kesovane = _naradiCache.GetAllChanges();
            foreach (var data in kesovane)
            {
                foreach (var naradi in data.IndexCislovane.Values)
                {
                    if (naradi.VOprave != null && string.Equals(naradi.VOprave.KodDodavatele, novy.Kod, StringComparison.Ordinal))
                        naradi.VOprave.NazevDodavatele = novy.Nazev;
                }
                foreach (var naradi in data.IndexPodleObjednavky.Values)
                {
                    if (naradi.VOprave != null && string.Equals(naradi.VOprave.KodDodavatele, novy.Kod, StringComparison.Ordinal))
                        naradi.VOprave.NazevDodavatele = novy.Nazev;
                }
            }

            var aktualizace = new AktualizaceReferenci(this, message.OnCompleted, message.OnError);
            _store.FindDocuments("dodavatele", novy.Kod, novy.Kod, aktualizace.Execute, message.OnError);
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

            var kesovane = _naradiCache.GetAllChanges();
            foreach (var data in kesovane)
            {
                foreach (var naradi in data.IndexCislovane.Values)
                {
                    if (naradi.NaVydejne != null && string.Equals(naradi.NaVydejne.KodVady, nova.Kod, StringComparison.Ordinal))
                        naradi.NaVydejne.NazevVady = nova.Nazev;
                }
            }

            var aktualizace = new AktualizaceReferenci(this, message.OnCompleted, message.OnError);
            _store.FindDocuments("vady", nova.Kod, nova.Kod, aktualizace.Execute, message.OnError);
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            DefinovanoPracovisteEvent existujici;
            var nove = message.Command;
            bool zmeneno = false;
            if (_ciselnikyData.IndexPracovist.TryGetValue(nove.Kod, out existujici))
            {
                zmeneno = true;
                _ciselnikyData.IndexPracovist[nove.Kod] = nove;
                _ciselnikyData.Pracoviste.Add(nove);
            }
            else
            {
                zmeneno = existujici.Nazev != nove.Nazev || existujici.Stredisko != nove.Stredisko;
                existujici.Nazev = nove.Nazev;
                existujici.Stredisko = nove.Stredisko;
            }
            _ciselnikyDirty |= zmeneno;

            var kesovane = _naradiCache.GetAllChanges();
            foreach (var data in kesovane)
            {
                foreach (var naradi in data.IndexCislovane.Values)
                {
                    if (naradi.VeVyrobe != null && string.Equals(naradi.VeVyrobe.KodPracoviste, nove.Kod, StringComparison.Ordinal))
                    {
                        naradi.VeVyrobe.NazevPracoviste = nove.Nazev;
                        naradi.VeVyrobe.StrediskoPracoviste = nove.Stredisko;
                    }
                }
                foreach (var naradi in data.IndexPodlePracoviste.Values)
                {
                    if (naradi.VeVyrobe != null && string.Equals(naradi.VeVyrobe.KodPracoviste, nove.Kod, StringComparison.Ordinal))
                    {
                        naradi.VeVyrobe.NazevPracoviste = nove.Nazev;
                        naradi.VeVyrobe.StrediskoPracoviste = nove.Stredisko;
                    }
                }
            }

            var aktualizace = new AktualizaceReferenci(this, message.OnCompleted, message.OnError);
            _store.FindDocuments("pracoviste", nove.Kod, nove.Kod, aktualizace.Execute, message.OnError);
        }

        private class AktualizaceReferenci
        {
            private Action _onComplete;
            private Action<Exception> _onError;
            private IList<string> _kody;
            private int _pozice;
            private DetailNaradiProjection _parent;

            public AktualizaceReferenci(DetailNaradiProjection parent, Action onComplete, Action<Exception> onError)
            {
                _parent = parent;
                _onComplete = onComplete;
                _onError = onError;
            }

            public void Execute(IList<string> kody)
            {
                _kody = kody;
                _pozice = 0;
                Iterace();
            }

            private void Iterace()
            {
                if (_pozice >= _kody.Count)
                    _onComplete();
                else
                {
                    var nazevDokumentu = _kody[_pozice];
                    _parent._naradiCache.Get(
                        _kody[_pozice],
                        (verze, data) =>
                        {
                            var zmeneno = AktualizovatNaradi(data);
                            if (zmeneno)
                                _parent._naradiCache.Insert(_kody[_pozice], verze, data, dirty: true);
                            Iterace();
                        }, _onError, _parent.NacistDataNaradi);
                }
            }

            private bool AktualizovatNaradi(DetailNaradiData data)
            {
                var zmeneno = false;

                foreach (var item in data.IndexCislovane.Values)
                    zmeneno = AktualizovatDetail(item) | zmeneno;
                foreach (var item in data.IndexPodleObjednavky.Values)
                    zmeneno = AktualizovatDetail(item) | zmeneno;
                foreach (var item in data.IndexPodlePracoviste.Values)
                    zmeneno = AktualizovatDetail(item) | zmeneno;
                foreach (var item in data.IndexPodleStavu.Values)
                    zmeneno = AktualizovatDetail(item) | zmeneno;

                return zmeneno;
            }

            private bool AktualizovatDetail(DetailNaradiCislovane item)
            {
                bool zmeneno = false;
                if (item.NaVydejne != null && item.NaVydejne.KodVady != null)
                {
                    DefinovanaVadaNaradiEvent vada;
                    if (_parent._ciselnikyData.IndexVad.TryGetValue(item.NaVydejne.KodVady, out vada))
                    {
                        if (!string.Equals(item.NaVydejne.NazevVady, vada.Nazev))
                        {
                            item.NaVydejne.NazevVady = vada.Nazev;
                            zmeneno = true;
                        }
                    }
                }
                if (item.VeVyrobe != null && item.VeVyrobe.KodPracoviste != null)
                {
                    DefinovanoPracovisteEvent pracoviste;
                    if (_parent._ciselnikyData.IndexPracovist.TryGetValue(item.VeVyrobe.KodPracoviste, out pracoviste))
                    {
                        if (!string.Equals(item.VeVyrobe.NazevPracoviste, pracoviste.Nazev))
                        {
                            item.VeVyrobe.NazevPracoviste = pracoviste.Nazev;
                            zmeneno = true;
                        }
                        if (!string.Equals(item.VeVyrobe.StrediskoPracoviste, pracoviste.Stredisko))
                        {
                            item.VeVyrobe.StrediskoPracoviste = pracoviste.Stredisko;
                            zmeneno = true;
                        }
                    }
                }
                if (item.VOprave != null && item.VOprave.KodDodavatele != null)
                {
                    DetailNaradiDataDodavatel dodavatel;
                    if (_parent._ciselnikyData.IndexDodavatelu.TryGetValue(item.VOprave.KodDodavatele, out dodavatel))
                    {
                        if (!string.Equals(item.VOprave.NazevDodavatele, dodavatel.Nazev))
                        {
                            item.VOprave.NazevDodavatele = dodavatel.Nazev;
                            zmeneno = true;
                        }
                    }
                } 
                return zmeneno;
            }

            private bool AktualizovatDetail(DetailNaradiNecislovane item)
            {
                bool zmeneno = false;
                if (item.VeVyrobe != null && item.VeVyrobe.KodPracoviste != null)
                {
                    DefinovanoPracovisteEvent pracoviste;
                    if (_parent._ciselnikyData.IndexPracovist.TryGetValue(item.VeVyrobe.KodPracoviste, out pracoviste))
                    {
                        if (!string.Equals(item.VeVyrobe.NazevPracoviste, pracoviste.Nazev))
                        {
                            item.VeVyrobe.NazevPracoviste = pracoviste.Nazev;
                            zmeneno = true;
                        }
                        if (!string.Equals(item.VeVyrobe.StrediskoPracoviste, pracoviste.Stredisko))
                        {
                            item.VeVyrobe.StrediskoPracoviste = pracoviste.Stredisko;
                            zmeneno = true;
                        }
                    }
                }
                if (item.VOprave != null && item.VOprave.KodDodavatele != null)
                {
                    DetailNaradiDataDodavatel dodavatel;
                    if (_parent._ciselnikyData.IndexDodavatelu.TryGetValue(item.VOprave.KodDodavatele, out dodavatel))
                    {
                        if (!string.Equals(item.VOprave.NazevDodavatele, dodavatel.Nazev))
                        {
                            item.VOprave.NazevDodavatele = dodavatel.Nazev;
                            zmeneno = true;
                        }
                    }
                } 
                return zmeneno;
            }
        }

        private DetailNaradiNaVydejne PrevodUmisteniNaVydejne(UmisteniNaradiDto umisteni, string kodVady)
        {
            if (umisteni == null || umisteni.ZakladniUmisteni != ZakladUmisteni.NaVydejne)
                return null;
            DefinovanaVadaNaradiEvent vada;
            var detail = new DetailNaradiNaVydejne();
            detail.KodVady = kodVady;
            if (kodVady != null && _ciselnikyData.IndexVad.TryGetValue(kodVady, out vada))
            {
                detail.NazevVady = vada.Nazev;
            }
            switch (umisteni.UpresneniZakladu)
            {
                case "VPoradku":
                    detail.StavNaradi = StavNaradi.VPoradku;
                    break;
                case "NutnoOpravit":
                    detail.StavNaradi = StavNaradi.NutnoOpravit;
                    break;
                case "Neopravitelne":
                    detail.StavNaradi = StavNaradi.Neopravitelne;
                    break;
                default:
                    detail.StavNaradi = StavNaradi.Neurcen;
                    break;
            }
            return detail;
        }

        private DetailNaradiVeVyrobe PrevodUmisteniVeVyrobe(UmisteniNaradiDto umisteni)
        {
            if (umisteni == null || umisteni.ZakladniUmisteni != ZakladUmisteni.VeVyrobe)
                return null;
            DefinovanoPracovisteEvent pracoviste;
            var detail = new DetailNaradiVeVyrobe();
            detail.KodPracoviste = umisteni.Pracoviste;
            if (_ciselnikyData.IndexPracovist.TryGetValue(umisteni.Pracoviste, out pracoviste))
            {
                detail.NazevPracoviste = pracoviste.Nazev;
                detail.StrediskoPracoviste = pracoviste.Stredisko;
            }
            return detail;
        }

        private DetailNaradiVOprave PrevodUmisteniVOprave(UmisteniNaradiDto umisteni, DateTime terminDodani)
        {
            if (umisteni == null || umisteni.ZakladniUmisteni != ZakladUmisteni.VOprave)
                return null;
            DetailNaradiDataDodavatel dodavatel;
            var detail = new DetailNaradiVOprave();
            detail.Objednavka = umisteni.Objednavka;
            detail.KodDodavatele = umisteni.Dodavatel;
            if (detail.KodDodavatele != null && _ciselnikyData.IndexDodavatelu.TryGetValue(detail.KodDodavatele, out dodavatel))
            {
                detail.NazevDodavatele = dodavatel.Nazev;
            }
            detail.TerminDodani = terminDodani;
            return detail;
        }

        private void UpravitPocty(DetailNaradiDataPocty pocty, UmisteniNaradiDto umisteni, int zmena)
        {
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            pocty.VPoradku += zmena;
                            break;
                        case "NutnoOpravit":
                            pocty.Poskozene += zmena;
                            break;
                        case "Neopravitelne":
                            pocty.Znicene += zmena;
                            break;
                    }
                    break;
                case ZakladUmisteni.VOprave:
                    pocty.VOprave += zmena;
                    break;
                case ZakladUmisteni.VeVyrobe:
                    pocty.VeVyrobe += zmena;
                    break;
            }
        }

        private void ZpracovatCislovaneNaradi(
            Guid naradiId, Action onCompleted, Action<Exception> onError, int cisloNaradi,
            UmisteniNaradiDto predchoziUmisteni, UmisteniNaradiDto noveUmisteni,
            string vada = null, DateTime terminDodani = default(DateTime))
        {
            ZpracovatNaradi(naradiId, onCompleted, onError, data =>
            {
                DetailNaradiCislovane cislovane;
                if (!data.IndexCislovane.TryGetValue(cisloNaradi, out cislovane))
                    return;

                var puvodniVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
                var puvodniPracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
                var puvodniDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

                cislovane.ZakladUmisteni = noveUmisteni.ZakladniUmisteni;
                cislovane.NaVydejne = PrevodUmisteniNaVydejne(noveUmisteni, vada);
                cislovane.VeVyrobe = PrevodUmisteniVeVyrobe(noveUmisteni);
                cislovane.VOprave = PrevodUmisteniVOprave(noveUmisteni, terminDodani);

                var novaVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
                var novePracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
                var novyDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

                if (puvodniVada != novaVada)
                    data.ReferenceVad = null;
                if (puvodniPracoviste != novePracoviste)
                    data.ReferencePracovist = null;
                if (puvodniDodavatel != novyDodavatel)
                    data.ReferenceDodavatelu = null;

                UpravitPocty(data.PoctyCelkem, predchoziUmisteni, -1);
                UpravitPocty(data.PoctyCelkem, noveUmisteni, 1);
                UpravitPocty(data.PoctyCislovane, predchoziUmisteni, -1);
                UpravitPocty(data.PoctyCislovane, noveUmisteni, 1);

                if (noveUmisteni.ZakladniUmisteni == ZakladUmisteni.VeSrotu)
                {
                    data.IndexCislovane.Remove(cisloNaradi);
                    data.Cislovane = null;
                }
            });
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                var cislovane = new DetailNaradiCislovane();
                cislovane.CisloNaradi = evnt.CisloNaradi;
                cislovane.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null);
                data.Cislovane = null;
                data.IndexCislovane[evnt.CisloNaradi] = cislovane;
                data.PoctyCelkem.VPoradku += 1;
                data.PoctyCislovane.VPoradku += 1;
            });
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatCislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatCislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, vada: evnt.KodVady);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            var evnt = message.Command;
            ZpracovatCislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, terminDodani: evnt.TerminDodani);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatCislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            var evnt = message.Command;
            ZpracovatCislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                DetailNaradiNecislovane naradi;
                if (!data.IndexPodleStavu.TryGetValue(StavNaradi.VPoradku, out naradi))
                {
                    naradi = new DetailNaradiNecislovane();
                    naradi.ZakladUmisteni = evnt.NoveUmisteni.ZakladniUmisteni;
                    naradi.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null);
                    data.IndexPodleStavu[StavNaradi.VPoradku] = naradi;
                    data.Necislovane = null;
                    data.PoctyCelkem.VPoradku += evnt.Pocet;
                    data.PoctyNecislovane.VPoradku += evnt.Pocet;
                }
                naradi.Pocet += evnt.Pocet;
            });
        }

        private void ZpracovatNecislovaneNaradi(
            Guid naradiId, Action onCompleted, Action<Exception> onError,
            UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, int pocet,
            DateTime terminDodani = default(DateTime))
        {
            ZpracovatNaradi(naradiId, onCompleted, onError, data =>
            {
                var proOdebrani = NajitNecislovane(data, predchozi, DateTime.MaxValue);
                var proPridani = NajitNecislovane(data, predchozi, terminDodani);
                proOdebrani.Pocet -= pocet;
                if (proOdebrani.Pocet == 0 || proPridani.Pocet == 0)
                    data.Necislovane = null;
                proPridani.Pocet += pocet;
                if (terminDodani != default(DateTime) && proPridani.VOprave != null)
                    proPridani.VOprave.TerminDodani = terminDodani;
                UpravitPocty(data.PoctyCelkem, predchozi, -pocet);
                UpravitPocty(data.PoctyNecislovane, predchozi, -pocet);
                UpravitPocty(data.PoctyCelkem, nove, pocet);
                UpravitPocty(data.PoctyNecislovane, nove, pocet);
            });
        }

        private DetailNaradiNecislovane NajitNecislovane(DetailNaradiData data, UmisteniNaradiDto umisteni, DateTime terminDodani)
        {
            DetailNaradiNecislovane naradi;
            StavNaradi stav;
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            stav = StavNaradi.VPoradku;
                            break;
                        case "NutnoOpravit":
                            stav = StavNaradi.NutnoOpravit;
                            break;
                        case "Neopravitelne":
                            stav = StavNaradi.Neopravitelne;
                            break;
                        default:
                            return null;
                    }
                    if (!data.IndexPodleStavu.TryGetValue(stav, out naradi))
                    {
                        naradi = new DetailNaradiNecislovane
                        {
                            ZakladUmisteni = ZakladUmisteni.NaVydejne,
                            NaVydejne = new DetailNaradiNaVydejne { StavNaradi = stav }
                        };
                        data.IndexPodleStavu[stav] = naradi;
                    }
                    return naradi;

                case ZakladUmisteni.VeVyrobe:
                    if (!data.IndexPodlePracoviste.TryGetValue(umisteni.Pracoviste, out naradi))
                    {
                        naradi = new DetailNaradiNecislovane
                        {
                            ZakladUmisteni = ZakladUmisteni.VeVyrobe,
                            VeVyrobe = PrevodUmisteniVeVyrobe(umisteni)
                        };
                        data.IndexPodlePracoviste[umisteni.Pracoviste] = naradi;
                    }
                    return naradi;

                case ZakladUmisteni.VOprave:
                    var klicObjednavky = KlicIndexuOprav(umisteni.Dodavatel, umisteni.Objednavka);
                    if (!data.IndexPodleObjednavky.TryGetValue(klicObjednavky, out naradi))
                    {
                        naradi = new DetailNaradiNecislovane
                        {
                            ZakladUmisteni = ZakladUmisteni.VOprave,
                            VOprave = PrevodUmisteniVOprave(umisteni, terminDodani)
                        };
                        data.IndexPodleObjednavky[klicObjednavky] = naradi;
                    }
                    return naradi;

                default:
                    return null;
            }
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNecislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNecislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNecislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet, terminDodani: evnt.TerminDodani);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNecislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            var evnt = message.Command;
            ZpracovatNecislovaneNaradi(evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet);
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
    }
    public class DetailNaradiDataDodavatel
    {
        public string Kod { get; set; }
        public string Nazev { get; set; }
    }
    public class DetailNaradiDataCiselniky
    {
        public List<DefinovanaVadaNaradiEvent> Vady { get; set; }
        public List<DetailNaradiDataDodavatel> Dodavatele { get; set; }
        public List<DefinovanoPracovisteEvent> Pracoviste { get; set; }
        public Dictionary<string, DefinovanaVadaNaradiEvent> IndexVad;
        public Dictionary<string, DetailNaradiDataDodavatel> IndexDodavatelu;
        public Dictionary<string, DefinovanoPracovisteEvent> IndexPracovist;
    }

    public class DetailNaradiSerializer
    {
        public string SerializeNaradi(DetailNaradiData data)
        {
            data.ReferenceDodavatelu = data.ReferenceDodavatelu ?? GenerovatReferenceDodavatelu(data);
            data.ReferencePracovist = data.ReferencePracovist ?? GenerovatReferencePracovist(data);
            data.ReferenceVad = data.ReferenceVad ?? GenerovatReferenceVad(data);
            if (data.Cislovane == null)
                data.Cislovane = data.IndexCislovane.Values.ToList();
            if (data.Necislovane == null)
            {
                data.Necislovane = new List<DetailNaradiNecislovane>();
                data.Necislovane.AddRange(data.IndexPodleStavu.Values.Where(n => n.Pocet > 0));
                data.Necislovane.AddRange(data.IndexPodlePracoviste.Values.Where(n => n.Pocet > 0));
                data.Necislovane.AddRange(data.IndexPodleObjednavky.Values.Where(n => n.Pocet > 0));
            }
            return JsonSerializer.SerializeToString(data);
        }
        private List<string> GenerovatReferenceDodavatelu(DetailNaradiData data)
        {
            var dodavatele = new HashSet<string>();
            foreach (var naradi in data.Necislovane)
            {
                if (naradi.VOprave != null && !string.IsNullOrEmpty(naradi.VOprave.KodDodavatele))
                    dodavatele.Add(naradi.VOprave.KodDodavatele);
            }
            foreach (var naradi in data.Cislovane)
            {
                if (naradi.VOprave != null && !string.IsNullOrEmpty(naradi.VOprave.KodDodavatele))
                    dodavatele.Add(naradi.VOprave.KodDodavatele);
            }
            return dodavatele.ToList();
        }
        private List<string> GenerovatReferencePracovist(DetailNaradiData data)
        {
            var pracoviste = new HashSet<string>();
            foreach (var naradi in data.Necislovane)
            {
                if (naradi.VeVyrobe != null && !string.IsNullOrEmpty(naradi.VeVyrobe.KodPracoviste))
                    pracoviste.Add(naradi.VeVyrobe.KodPracoviste);
            }
            foreach (var naradi in data.Cislovane)
            {
                if (naradi.VeVyrobe != null && !string.IsNullOrEmpty(naradi.VeVyrobe.KodPracoviste))
                    pracoviste.Add(naradi.VeVyrobe.KodPracoviste);
            }
            return pracoviste.ToList();
        }
        private List<string> GenerovatReferenceVad(DetailNaradiData data)
        {
            var vady = new HashSet<string>();
            foreach (var naradi in data.Necislovane)
            {
                if (naradi.NaVydejne != null && !string.IsNullOrEmpty(naradi.NaVydejne.KodVady))
                    vady.Add(naradi.NaVydejne.KodVady);
            }
            foreach (var naradi in data.Cislovane)
            {
                if (naradi.NaVydejne != null && !string.IsNullOrEmpty(naradi.NaVydejne.KodVady))
                    vady.Add(naradi.NaVydejne.KodVady);
            }
            return vady.ToList();
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
            ciselniky.Dodavatele = ciselniky.Dodavatele ?? new List<DetailNaradiDataDodavatel>();
            ciselniky.IndexDodavatelu = ciselniky.Dodavatele.ToDictionary(d => d.Kod);
            ciselniky.IndexPracovist = ciselniky.Pracoviste.ToDictionary(p => p.Kod);
            ciselniky.IndexVad = ciselniky.Vady.ToDictionary(v => v.Kod);
            return ciselniky;
        }

        public IList<DocumentIndexing> IndexNaradi(DetailNaradiData data)
        {
            var indexy = new List<DocumentIndexing>();
            indexy.Add(new DocumentIndexing("vady", data.ReferenceVad));
            indexy.Add(new DocumentIndexing("pracoviste", data.ReferencePracovist));
            indexy.Add(new DocumentIndexing("dodavatele", data.ReferenceDodavatelu));
            return indexy;
        }
    }
}