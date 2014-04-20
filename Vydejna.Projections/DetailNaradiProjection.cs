using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vydejna.Contracts;

namespace Vydejna.Projections.DetailNaradiReadModel
{
    public class DetailNaradiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
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
        private DetailNaradiRepository _repository;
        private MemoryCache<DetailNaradiDataDetail> _cacheDetail;
        private MemoryCache<DetailNaradiDataDodavatele> _cacheDodavatele;
        private MemoryCache<DetailNaradiDataVady> _cacheVady;
        private MemoryCache<DefinovanoPracovisteEvent> _cachePracoviste;

        public DetailNaradiProjection(DetailNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheDetail = new MemoryCache<DetailNaradiDataDetail>(executor, time);
            _cacheDodavatele = new MemoryCache<DetailNaradiDataDodavatele>(executor, time);
            _cachePracoviste = new MemoryCache<DefinovanoPracovisteEvent>(executor, time);
            _cacheVady = new MemoryCache<DetailNaradiDataVady>(executor, time);
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
            _cacheDetail.Clear();
            _cacheDodavatele.Clear();
            _cachePracoviste.Clear();
            _cacheVady.Clear();
            _repository.Reset(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
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
                KorekceDetailuPredUlozenim();
                FlushDodavatelu();
            }
            private void KorekceDetailuPredUlozenim()
            {
                foreach (var detail in _parent._cacheDetail.GetAllChanges())
                {
                    if (detail.Cislovane == null)
                        detail.Cislovane = new List<DetailNaradiCislovane>(detail.IndexCislovane.Count);
                    else
                        detail.Cislovane.Clear();
                    detail.Cislovane.AddRange(detail.IndexCislovane.Values);

                    if (detail.Necislovane == null)
                        detail.Necislovane = new List<DetailNaradiNecislovane>();
                    else
                        detail.Necislovane.Clear();
                    detail.Necislovane.AddRange(detail.IndexPodleStavu.Values);
                    detail.Necislovane.AddRange(detail.IndexPodlePracoviste.Values);
                    detail.Necislovane.AddRange(detail.IndexPodleObjednavky.Values);

                    detail.ReferenceDodavatelu = detail.ReferenceDodavatelu ?? GenerovatReferenceDodavatelu(detail);
                    detail.ReferencePracovist = detail.ReferencePracovist ?? GenerovatReferencePracovist(detail);
                    detail.ReferenceVad = detail.ReferenceVad ?? GenerovatReferenceVad(detail);
                }
            }
            private void FlushDodavatelu()
            {
                _parent._cacheDodavatele.Flush(FlushPracovist, _onError,
                    save => _parent._repository.UlozitDodavatele(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
            private void FlushPracovist()
            {
                _parent._cachePracoviste.Flush(FlushVad, _onError,
                    save => _parent._repository.UlozitPracoviste(save.Key, save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
            private void FlushVad()
            {
                _parent._cacheVady.Flush(FlushDetailu, _onError,
                    save => _parent._repository.UlozitVadu(save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
            private void FlushDetailu()
            {
                _parent._cacheDetail.Flush(_onCompleted, _onError,
                    save => _parent._repository.UlozitDetail(save.Value.NaradiId, save.Version, save.Value, save.SavedAsVersion, save.SavingFailed));
            }
        }

        private void ZpracovatHoleNaradi(Guid naradiId, Action onCompleted, Action<Exception> onError, Action<DetailNaradiDataDetail> updateAction)
        {
            _cacheDetail.Get(
                naradiId.ToString("N"),
                (verze, data) =>
                {
                    updateAction(data);
                    _cacheDetail.Insert(naradiId.ToString("N"), verze, data, dirty: true);
                    onCompleted();
                }, onError,
                load => _repository.NacistDetail(naradiId, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                    load.ValueIsStillValid, load.LoadingFailed));
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            var evnt = message.Command;
            ZpracovatHoleNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
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
            ZpracovatHoleNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                data.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<DeaktivovanoNaradiEvent> message)
        {
            var evnt = message.Command;
            ZpracovatHoleNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                data.Aktivni = true;
            });
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            _cacheDodavatele.Get("dodavatele",
                (verze, ciselnik) =>
                {
                    DetailNaradiDataDodavatel existujici;
                    var novy = message.Command;
                    bool zmeneno = false;

                    if (!ciselnik.IndexDodavatelu.TryGetValue(novy.Kod, out existujici))
                    {
                        zmeneno = true;
                        existujici = new DetailNaradiDataDodavatel();
                        existujici.Kod = novy.Kod;
                        existujici.Nazev = novy.Nazev;
                        ciselnik.IndexDodavatelu[novy.Kod] = existujici;
                        ciselnik.Dodavatele.Add(existujici);
                    }
                    else
                    {
                        zmeneno = existujici.Nazev != novy.Nazev;
                        existujici.Nazev = novy.Nazev;
                    }
                    if (zmeneno)
                        _cacheDodavatele.Insert("dodavatele", verze, ciselnik, dirty: true);
                    message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistDodavatele(
                    (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                    load.LoadingFailed)
                );
        }

        public void Handle(CommandExecution<DefinovanaVadaNaradiEvent> message)
        {
            _cacheVady.Get("vady",
                (verze, ciselnik) =>
                {
                    DefinovanaVadaNaradiEvent existujici;
                    var nova = message.Command;
                    bool zmeneno = false;
                    if (ciselnik == null)
                    {
                        ciselnik = new DetailNaradiDataVady();
                        ciselnik.Vady = new List<DefinovanaVadaNaradiEvent>();
                        ciselnik.IndexVad = new Dictionary<string, DefinovanaVadaNaradiEvent>();
                    }
                    else if (ciselnik.IndexVad == null)
                    {
                        ciselnik.IndexVad = new Dictionary<string, DefinovanaVadaNaradiEvent>();
                        foreach (var vada in ciselnik.Vady)
                            ciselnik.IndexVad[vada.Kod] = vada;
                    }

                    if (ciselnik.IndexVad.TryGetValue(nova.Kod, out existujici))
                    {
                        zmeneno = true;
                        ciselnik.IndexVad[nova.Kod] = nova;
                        ciselnik.Vady.Add(nova);
                    }
                    else
                    {
                        zmeneno = existujici.Nazev != nova.Nazev;
                        existujici.Nazev = nova.Nazev;
                    }
                    if (zmeneno)
                        _cacheVady.Insert("vady", verze, ciselnik, dirty: true);
                    message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistVady(
                    (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                    load.LoadingFailed));
        }

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            _cachePracoviste.Get(
                message.Command.Kod,
                (verze, existujici) =>
                {
                    var nove = message.Command;
                    bool zmeneno = false;
                    if (existujici != null)
                    {
                        zmeneno = existujici.Nazev != nove.Nazev || existujici.Stredisko != nove.Stredisko;
                        existujici.Nazev = nove.Nazev;
                        existujici.Stredisko = nove.Stredisko;
                    }
                    else
                    {
                        zmeneno = true;
                        existujici = nove;
                    }
                    if (zmeneno)
                        _cachePracoviste.Insert(existujici.Kod, verze, existujici, dirty: true);
                    message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistPracoviste(load.Key, load.SetLoadedValue, load.LoadingFailed));
        }

        private static bool UmisteniNaVydejne(UmisteniNaradiDto umisteni)
        {
            return umisteni != null && umisteni.ZakladniUmisteni == ZakladUmisteni.NaVydejne;
        }

        private static DetailNaradiNaVydejne PrevodUmisteniNaVydejne(UmisteniNaradiDto umisteni, string kodVady, DefinovanaVadaNaradiEvent vada)
        {
            if (!UmisteniNaVydejne(umisteni))
                return null;
            var detail = new DetailNaradiNaVydejne();
            detail.KodVady = kodVady;
            if (vada != null)
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

        private static bool UmisteniVeVyrobe(UmisteniNaradiDto umisteni)
        {
            if (umisteni == null)
                return false;
            if (umisteni.ZakladniUmisteni == ZakladUmisteni.VeVyrobe)
                return true;
            if (umisteni.ZakladniUmisteni == ZakladUmisteni.Spotrebovano)
                return true;
            return false;
        }

        private static DetailNaradiVeVyrobe PrevodUmisteniVeVyrobe(UmisteniNaradiDto umisteni, DefinovanoPracovisteEvent pracoviste)
        {
            if (!UmisteniVeVyrobe(umisteni))
                return null;
            var detail = new DetailNaradiVeVyrobe();
            detail.KodPracoviste = umisteni.Pracoviste;
            if (pracoviste != null)
            {
                detail.NazevPracoviste = pracoviste.Nazev;
                detail.StrediskoPracoviste = pracoviste.Stredisko;
            }
            return detail;
        }

        private static bool UmisteniVOprave(UmisteniNaradiDto umisteni)
        {
            return umisteni != null && umisteni.ZakladniUmisteni == ZakladUmisteni.VOprave;
        }

        private static DetailNaradiVOprave PrevodUmisteniVOprave(UmisteniNaradiDto umisteni, DateTime terminDodani, DetailNaradiDataDodavatel dodavatel)
        {
            if (!UmisteniVOprave(umisteni))
                return null;
            var detail = new DetailNaradiVOprave();
            detail.Objednavka = umisteni.Objednavka;
            detail.KodDodavatele = umisteni.Dodavatel;
            if (detail.KodDodavatele != null && dodavatel != null)
            {
                detail.NazevDodavatele = dodavatel.Nazev;
            }
            detail.TerminDodani = terminDodani;
            return detail;
        }

        private static void UpravitPocty(DetailNaradiPocty pocty, UmisteniNaradiDto umisteni, int zmena)
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

        private static DetailNaradiDataVady RozsiritData(DetailNaradiDataVady data)
        {
            if (data == null)
            {
                data = new DetailNaradiDataVady();
                data.Vady = new List<DefinovanaVadaNaradiEvent>();
                data.IndexVad = new Dictionary<string, DefinovanaVadaNaradiEvent>();
            }
            else if (data.IndexVad == null)
            {
                data.IndexVad = new Dictionary<string, DefinovanaVadaNaradiEvent>();
                foreach (var vada in data.Vady)
                    data.IndexVad[vada.Kod] = vada;
            }
            return data;
        }

        private static DetailNaradiDataDodavatele RozsiritData(DetailNaradiDataDodavatele data)
        {
            if (data == null)
            {
                data = new DetailNaradiDataDodavatele();
                data.Dodavatele = new List<DetailNaradiDataDodavatel>();
                data.IndexDodavatelu = new Dictionary<string, DetailNaradiDataDodavatel>();
            }
            else if (data.IndexDodavatelu == null)
            {
                data.IndexDodavatelu = new Dictionary<string, DetailNaradiDataDodavatel>();
                foreach (var dodavatel in data.Dodavatele)
                    data.IndexDodavatelu[dodavatel.Kod] = dodavatel;
            }
            return data;
        }

        private static DetailNaradiDataDetail RozsiritData(DetailNaradiDataDetail data)
        {
            if (data == null)
            {
                data = new DetailNaradiDataDetail();
                data.PoctyCelkem = new DetailNaradiPocty();
                data.PoctyCislovane = new DetailNaradiPocty();
                data.PoctyNecislovane = new DetailNaradiPocty();
                data.Cislovane = new List<DetailNaradiCislovane>();
                data.Necislovane = new List<DetailNaradiNecislovane>();
                data.ReferenceDodavatelu = new List<string>();
                data.ReferencePracovist = new List<string>();
                data.ReferenceVad = new List<string>();
                data.IndexCislovane = new Dictionary<int, DetailNaradiCislovane>();
                data.IndexPodleObjednavky = new Dictionary<string, DetailNaradiNecislovane>();
                data.IndexPodlePracoviste = new Dictionary<string, DetailNaradiNecislovane>();
                data.IndexPodleStavu = new Dictionary<StavNaradi, DetailNaradiNecislovane>();
            }
            else
            {
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
            }
            return data;
        }

        private class ZpracovatCislovaneNaradi
        {
            private DetailNaradiProjection _parent;
            private Guid _naradiId;
            private Action _onCompleted;
            private Action<Exception> _onError;
            private int _cisloNaradi;
            private UmisteniNaradiDto _predchoziUmisteni;
            private UmisteniNaradiDto _noveUmisteni;
            private DateTime _terminDodani;

            private string _kodVady;
            private string _kodDodavatele;
            private string _kodPracoviste;

            private DefinovanaVadaNaradiEvent _nactenaVada;
            private DetailNaradiDataDodavatel _nactenyDodavatel;
            private DefinovanoPracovisteEvent _nactenePracoviste;
            private DetailNaradiDataDetail _nactenyDetail;
            private int _verzeDetailu;

            public ZpracovatCislovaneNaradi(
                DetailNaradiProjection parent,
                Guid naradiId, Action onCompleted, Action<Exception> onError, int cisloNaradi,
                UmisteniNaradiDto predchoziUmisteni, UmisteniNaradiDto noveUmisteni,
                string kodVady = null, DateTime terminDodani = default(DateTime))
            {
                _parent = parent;
                _naradiId = naradiId;
                _onCompleted = onCompleted;
                _onError = onError;
                _cisloNaradi = cisloNaradi;
                _predchoziUmisteni = predchoziUmisteni;
                _noveUmisteni = noveUmisteni;
                _kodVady = kodVady;
                _terminDodani = terminDodani;
            }

            public void Execute()
            {
                _kodPracoviste = UmisteniVeVyrobe(_noveUmisteni) ? _noveUmisteni.Pracoviste : null;
                _kodDodavatele = UmisteniVOprave(_noveUmisteni) ? _noveUmisteni.Dodavatel : null;

                _parent._cacheDetail.Get(_naradiId.ToString("N"), NactenDetail, _onError,
                    load => _parent._repository.NacistDetail(_naradiId, load.OldVersion,
                        (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                        load.ValueIsStillValid, load.LoadingFailed));
            }
            private void NactenDetail(int verze, DetailNaradiDataDetail data)
            {
                _verzeDetailu = verze;
                _nactenyDetail = data;
                NacistVady();
            }

            private void NacistVady()
            {
                if (_kodVady != null)
                {
                    _parent._cacheVady.Get("vady", NactenyVady, _onError,
                        load => _parent._repository.NacistVady(
                            (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                            load.LoadingFailed));
                }
                else
                    NactenyVady(0, null);
            }

            private void NactenyVady(int verze, DetailNaradiDataVady data)
            {
                if (data != null && _kodVady != null)
                    data.IndexVad.TryGetValue(_kodVady, out _nactenaVada);
                NacistDodavatele();
            }

            private void NacistDodavatele()
            {
                if (_kodDodavatele != null)
                {
                    _parent._cacheDodavatele.Get("dodavatele", NacteniDodavatele, _onError,
                        load => _parent._repository.NacistDodavatele(
                            (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                            load.LoadingFailed));
                }
                else
                    NacteniDodavatele(0, null);
            }

            private void NacteniDodavatele(int verze, DetailNaradiDataDodavatele data)
            {
                if (data != null && _kodDodavatele != null)
                    data.IndexDodavatelu.TryGetValue(_kodDodavatele, out _nactenyDodavatel);
                NacistPracoviste();
            }

            private void NacistPracoviste()
            {
                if (_kodPracoviste != null)
                {
                    _parent._cachePracoviste.Get(_kodPracoviste, NactenoPracoviste, _onError,
                        load => _parent._repository.NacistPracoviste(_kodPracoviste, load.SetLoadedValue, load.LoadingFailed));
                }
                else
                    NactenoPracoviste(0, null);
            }

            private void NactenoPracoviste(int verze, DefinovanoPracovisteEvent data)
            {
                _nactenePracoviste = data;
                ExecuteInternal();
            }

            private void ExecuteInternal()
            {
                DetailNaradiCislovane cislovane;
                if (!_nactenyDetail.IndexCislovane.TryGetValue(_cisloNaradi, out cislovane))
                    return;

                var puvodniVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
                var puvodniPracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
                var puvodniDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

                cislovane.ZakladUmisteni = _noveUmisteni.ZakladniUmisteni;
                cislovane.NaVydejne = PrevodUmisteniNaVydejne(_noveUmisteni, _kodVady, _nactenaVada);
                cislovane.VeVyrobe = PrevodUmisteniVeVyrobe(_noveUmisteni, _nactenePracoviste);
                cislovane.VOprave = PrevodUmisteniVOprave(_noveUmisteni, _terminDodani, _nactenyDodavatel);

                var novaVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
                var novePracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
                var novyDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

                UpravitPocty(_nactenyDetail.PoctyCelkem, _predchoziUmisteni, -1);
                UpravitPocty(_nactenyDetail.PoctyCelkem, _noveUmisteni, 1);
                UpravitPocty(_nactenyDetail.PoctyCislovane, _predchoziUmisteni, -1);
                UpravitPocty(_nactenyDetail.PoctyCislovane, _noveUmisteni, 1);

                if (puvodniVada != novaVada)
                    _nactenyDetail.ReferenceVad = null;
                if (puvodniPracoviste != novePracoviste)
                    _nactenyDetail.ReferencePracovist = null;
                if (puvodniDodavatel != novyDodavatel)
                    _nactenyDetail.ReferenceDodavatelu = null;

                if (_noveUmisteni.ZakladniUmisteni == ZakladUmisteni.VeSrotu)
                {
                    _nactenyDetail.IndexCislovane.Remove(_cisloNaradi);
                    _nactenyDetail.Cislovane = null;
                }

                _parent._cacheDetail.Insert(_naradiId.ToString("N"), _verzeDetailu, _nactenyDetail, dirty: true);
                _onCompleted();
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            var evnt = message.Command;
            ZpracovatHoleNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                var cislovane = new DetailNaradiCislovane();
                cislovane.CisloNaradi = evnt.CisloNaradi;
                cislovane.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null, null);
                data.Cislovane = null;
                data.IndexCislovane[evnt.CisloNaradi] = cislovane;
                data.PoctyCelkem.VPoradku += 1;
                data.PoctyCislovane.VPoradku += 1;
            });
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatCislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatCislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, kodVady: evnt.KodVady).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatCislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, terminDodani: evnt.TerminDodani).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatCislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni).Execute();
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatCislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            var evnt = message.Command;
            ZpracovatHoleNaradi(evnt.NaradiId, message.OnCompleted, message.OnError, data =>
            {
                DetailNaradiNecislovane naradi;
                if (!data.IndexPodleStavu.TryGetValue(StavNaradi.VPoradku, out naradi))
                {
                    naradi = new DetailNaradiNecislovane();
                    naradi.ZakladUmisteni = evnt.NoveUmisteni.ZakladniUmisteni;
                    naradi.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null, null);
                    data.IndexPodleStavu[StavNaradi.VPoradku] = naradi;
                    data.Necislovane = null;
                    data.PoctyCelkem.VPoradku += evnt.Pocet;
                    data.PoctyNecislovane.VPoradku += evnt.Pocet;
                }
                naradi.Pocet += evnt.Pocet;
            });
        }

        private class ZpracovatNecislovaneNaradi
        {
            private DetailNaradiProjection _parent;
            private Guid _naradiId;
            private Action _onCompleted;
            private Action<Exception> _onError;
            private UmisteniNaradiDto _predchozi;
            private UmisteniNaradiDto _nove;
            private int _pocet;
            private DateTime _terminDodani;

            private string _kodDodavatele;
            private string _kodPracoviste;

            private DetailNaradiDataDodavatel _nactenyDodavatel;
            private DefinovanoPracovisteEvent _nactenePracoviste;
            private DetailNaradiDataDetail _nactenyDetail;
            private int _verzeDetailu;

            public ZpracovatNecislovaneNaradi(
                DetailNaradiProjection parent,
                Guid naradiId, Action onCompleted, Action<Exception> onError,
                UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, int pocet,
                DateTime terminDodani = default(DateTime))
            {
                _parent = parent;
                _naradiId = naradiId;
                _onCompleted = onCompleted;
                _onError = onError;
                _predchozi = predchozi;
                _nove = nove;
                _pocet = pocet;
                _terminDodani = terminDodani;
            }

            public void Execute()
            {
                _kodPracoviste = UmisteniVeVyrobe(_nove) ? _nove.Pracoviste : null;
                _kodDodavatele = UmisteniVOprave(_nove) ? _nove.Dodavatel : null;

                _parent._cacheDetail.Get(
                    _naradiId.ToString("N"),
                    NactenDetail,
                    _onError,
                    load => _parent._repository.NacistDetail(_naradiId, load.OldVersion,
                        (verze, data) => load.SetLoadedValue(verze, RozsiritData(data)),
                        load.ValueIsStillValid, load.LoadingFailed)
                    );
            }

            private void NactenDetail(int verze, DetailNaradiDataDetail data)
            {
                _verzeDetailu = verze;
                _nactenyDetail = data;

                if (_kodDodavatele != null)
                {
                    _parent._cacheDodavatele.Get("dodavatele", NacteniDodavatele, _onError,
                        load => _parent._repository.NacistDodavatele((v, d) => load.SetLoadedValue(v, RozsiritData(d)), load.LoadingFailed));
                }
                else
                    NacteniDodavatele(0, null);
            }

            private void NacteniDodavatele(int verze, DetailNaradiDataDodavatele data)
            {
                if (data != null && _kodDodavatele != null)
                    data.IndexDodavatelu.TryGetValue(_kodDodavatele, out _nactenyDodavatel);

                if (_kodPracoviste != null)
                {
                    _parent._cachePracoviste.Get(
                        _kodPracoviste, NactenoPracoviste, _onError,
                        load => _parent._repository.NacistPracoviste(_kodPracoviste, load.SetLoadedValue, load.LoadingFailed)
                        );
                }
                else
                    NactenoPracoviste(0, null);
            }

            private void NactenoPracoviste(int verze, DefinovanoPracovisteEvent data)
            {
                _nactenePracoviste = data;
                ExecuteInternal();
                _parent._cacheDetail.Insert(_naradiId.ToString("N"), _verzeDetailu, _nactenyDetail, dirty: true);
                _onCompleted();
            }

            private void ExecuteInternal()
            {
                var proOdebrani = NajitNecislovane(_nactenyDetail, _predchozi, DateTime.MaxValue, false);
                var proPridani = NajitNecislovane(_nactenyDetail, _nove, _terminDodani, true);

                if (proPridani.VeVyrobe != null && _nactenePracoviste != null)
                {
                    proPridani.VeVyrobe.NazevPracoviste = _nactenePracoviste.Nazev;
                    proPridani.VeVyrobe.StrediskoPracoviste = _nactenePracoviste.Stredisko;
                }
                if (proPridani.VOprave != null && _nactenyDodavatel != null)
                {
                    proPridani.VOprave.NazevDodavatele = _nactenyDodavatel.Nazev;
                }

                if (proOdebrani != null)
                {
                    proOdebrani.Pocet -= _pocet;
                    if (proOdebrani.Pocet == 0 || proPridani.Pocet == 0)
                        _nactenyDetail.Necislovane = null;
                }
                proPridani.Pocet += _pocet;
                if (_terminDodani != default(DateTime) && proPridani.VOprave != null)
                    proPridani.VOprave.TerminDodani = _terminDodani;
                UpravitPocty(_nactenyDetail.PoctyCelkem, _predchozi, -_pocet);
                UpravitPocty(_nactenyDetail.PoctyNecislovane, _predchozi, -_pocet);
                UpravitPocty(_nactenyDetail.PoctyCelkem, _nove, _pocet);
                UpravitPocty(_nactenyDetail.PoctyNecislovane, _nove, _pocet);
            }

            private DetailNaradiNecislovane NajitNecislovane(DetailNaradiDataDetail data, UmisteniNaradiDto umisteni, DateTime terminDodani, bool vytvoritPokudChybi)
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
                        if (!data.IndexPodleStavu.TryGetValue(stav, out naradi) && vytvoritPokudChybi)
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
                        if (!data.IndexPodlePracoviste.TryGetValue(umisteni.Pracoviste, out naradi) && vytvoritPokudChybi)
                        {
                            naradi = new DetailNaradiNecislovane
                            {
                                ZakladUmisteni = ZakladUmisteni.VeVyrobe,
                                VeVyrobe = PrevodUmisteniVeVyrobe(umisteni, null)
                            };
                            data.IndexPodlePracoviste[umisteni.Pracoviste] = naradi;
                        }
                        return naradi;

                    case ZakladUmisteni.VOprave:
                        var klicObjednavky = KlicIndexuOprav(umisteni.Dodavatel, umisteni.Objednavka);
                        if (!data.IndexPodleObjednavky.TryGetValue(klicObjednavky, out naradi) && vytvoritPokudChybi)
                        {
                            naradi = new DetailNaradiNecislovane
                            {
                                ZakladUmisteni = ZakladUmisteni.VOprave,
                                VOprave = PrevodUmisteniVOprave(umisteni, terminDodani, null)
                            };
                            data.IndexPodleObjednavky[klicObjednavky] = naradi;
                        }
                        return naradi;

                    default:
                        return null;
                }
            }
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatNecislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatNecislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatNecislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet, terminDodani: evnt.TerminDodani).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatNecislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet).Execute();
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            var evnt = message.Command;
            new ZpracovatNecislovaneNaradi(this, evnt.NaradiId, message.OnCompleted, message.OnError,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet).Execute();
        }

        private static List<string> GenerovatReferenceDodavatelu(DetailNaradiDataDetail data)
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
        private static List<string> GenerovatReferencePracovist(DetailNaradiDataDetail data)
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
        private static List<string> GenerovatReferenceVad(DetailNaradiDataDetail data)
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
    }

    public class DetailNaradiDataDetail
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }
        public int NaSklade { get; set; }

        public DetailNaradiPocty PoctyCelkem { get; set; }
        public DetailNaradiPocty PoctyNecislovane { get; set; }
        public DetailNaradiPocty PoctyCislovane { get; set; }

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
    public class DetailNaradiDataVady
    {
        public List<DefinovanaVadaNaradiEvent> Vady { get; set; }
        public Dictionary<string, DefinovanaVadaNaradiEvent> IndexVad;
    }
    public class DetailNaradiDataDodavatele
    {
        public List<DetailNaradiDataDodavatel> Dodavatele { get; set; }
        public Dictionary<string, DetailNaradiDataDodavatel> IndexDodavatelu;
    }

    public class DetailNaradiRepository
    {
        private IDocumentFolder _folder;

        public DetailNaradiRepository(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public void Reset(Action onComplete, Action<Exception> onError)
        {
            _folder.DeleteAll(onComplete, onError);
        }

        public void NacistDetail(Guid naradiId, int oldVersion, Action<int, DetailNaradiDataDetail> onLoaded, Action onValid, Action<Exception> onError)
        {
            _folder.GetNewerDocument(
                string.Concat("detail-", naradiId.ToString("N")),
                oldVersion,
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<DetailNaradiDataDetail>(raw)),
                () => onValid(),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitDetail(Guid naradiId, int verze, DetailNaradiDataDetail data, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                string.Concat("detail-", naradiId.ToString("N")),
                JsonSerializer.SerializeToString(data),
                DocumentStoreVersion.At(verze),
                IndexyDetailu(data),
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex)
                );
        }

        private IList<DocumentIndexing> IndexyDetailu(DetailNaradiDataDetail data)
        {
            var indexy = new List<DocumentIndexing>(4);
            indexy.Add(new DocumentIndexing("vady", data.ReferenceVad));
            indexy.Add(new DocumentIndexing("pracoviste", data.ReferencePracovist));
            indexy.Add(new DocumentIndexing("dodavatele", data.ReferenceDodavatelu));
            return indexy;
        }

        public void NajitDetailyPodleDodavatele(string kodDodavatele, Action<IList<Guid>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("dodavatele", kodDodavatele, kodDodavatele, nalezene => onNalezenSeznam(VytvoritSeznamNaradi(nalezene)), onError);
        }

        private IList<Guid> VytvoritSeznamNaradi(IList<string> nalezene)
        {
            var result = new List<Guid>(nalezene.Count);
            foreach (var retezec in nalezene)
            {
                Guid guid;
                if (retezec.StartsWith("naradi-") && Guid.TryParseExact(retezec.Substring(7), "N", out guid))
                    result.Add(guid);
            }
            return result;
        }

        public void NacistDodavatele(Action<int, DetailNaradiDataDodavatele> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                "dodavatele",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<DetailNaradiDataDodavatele>(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitDodavatele(int verze, DetailNaradiDataDodavatele ciselnik, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument("dodavatele",
                JsonSerializer.SerializeToString(ciselnik),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NacistVady(Action<int, DetailNaradiDataVady> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument("vady",
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<DetailNaradiDataVady>(raw)),
                () => onLoaded(0, null),
                ex => onError(ex));
        }

        public void UlozitVadu(int verze, DetailNaradiDataVady ciselnik, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument("vady",
                JsonSerializer.SerializeToString(ciselnik),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex));
        }

        public void NajitDetailyPodleVady(string kodVady, Action<IList<Guid>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("vady", kodVady, kodVady, nalezene => onNalezenSeznam(VytvoritSeznamNaradi(nalezene)), onError);
        }

        public void NajitDetailyPodlePracoviste(string kodPracoviste, Action<IList<Guid>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("pracoviste", kodPracoviste, kodPracoviste, nalezene => onNalezenSeznam(VytvoritSeznamNaradi(nalezene)), onError);
        }

        public void NacistPracoviste(string kodPracoviste, Action<int, DefinovanoPracovisteEvent> onLoaded, Action<Exception> onError)
        {
            _folder.GetDocument(
                "pracoviste-" + kodPracoviste,
                (verze, raw) => onLoaded(verze, string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<DefinovanoPracovisteEvent>(raw)),
                () => onLoaded(0, null), ex => onError(ex));
        }

        public void UlozitPracoviste(string kodPracoviste, int verze, DefinovanoPracovisteEvent pracoviste, Action<int> onSaved, Action<Exception> onError)
        {
            _folder.SaveDocument(
                "pracoviste-" + kodPracoviste,
                JsonSerializer.SerializeToString(pracoviste),
                DocumentStoreVersion.At(verze),
                null,
                () => onSaved(verze + 1),
                () => onError(new ProjectorMessages.ConcurrencyException()),
                ex => onError(ex)
                );
        }
    }

    public class DetailNaradiReader
        : IAnswer<DetailNaradiRequest, DetailNaradiResponse>
    {
        private DetailNaradiRepository _repository;
        private MemoryCache<DetailNaradiResponse> _cacheDetaily;

        public DetailNaradiReader(DetailNaradiRepository repository, IQueueExecution executor, ITime time)
        {
            _repository = repository;
            _cacheDetaily = new MemoryCache<DetailNaradiResponse>(executor, time);
        }

        public void Handle(QueryExecution<DetailNaradiRequest, DetailNaradiResponse> message)
        {
            _cacheDetaily.Get(
                message.Request.NaradiId.ToString("N"),
                (verze, data) => message.OnCompleted(data),
                message.OnError,
                load => _repository.NacistDetail(
                    message.Request.NaradiId, load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponse(message.Request, data)),
                    load.ValueIsStillValid, load.LoadingFailed)
                );
        }

        private DetailNaradiResponse VytvoritResponse(DetailNaradiRequest request, DetailNaradiDataDetail data)
        {
            var response = new DetailNaradiResponse();
            if (data == null)
            {
                response.NaradiId = request.NaradiId;
                response.Cislovane = new List<DetailNaradiCislovane>();
                response.Necislovane = new List<DetailNaradiNecislovane>();
                response.PoctyCelkem = new DetailNaradiPocty();
                response.PoctyCislovane = new DetailNaradiPocty();
                response.PoctyNecislovane = new DetailNaradiPocty();
                response.Vykres = response.Rozmer = response.Druh = "";
            }
            else
            {
                response.NaradiId = data.NaradiId;
                response.Vykres = data.Vykres;
                response.Rozmer = data.Rozmer;
                response.Druh = data.Druh;
                response.Aktivni = data.Aktivni;

                response.Cislovane = data.Cislovane;
                response.Necislovane = data.Necislovane;

                response.NaSklade = data.NaSklade;
                response.PoctyCelkem = data.PoctyCelkem;
                response.PoctyCislovane = data.PoctyCislovane;
                response.PoctyNecislovane = data.PoctyNecislovane;
            }
            return response;
        }
    }
}