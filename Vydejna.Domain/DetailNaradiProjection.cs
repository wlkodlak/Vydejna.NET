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
                FlushDodavatelu();
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
                load => _repository.NacistDetail(naradiId, load.OldVersion, load.SetLoadedValue, load.ValueIsStillValid, load.LoadingFailed));
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
                    if (ciselnik == null)
                    {
                        ciselnik = new DetailNaradiDataDodavatele();
                        ciselnik.Dodavatele = new List<DetailNaradiDataDodavatel>();
                        ciselnik.IndexDodavatelu = new Dictionary<string, DetailNaradiDataDodavatel>();
                    }
                    else if (ciselnik.IndexDodavatelu == null)
                    {
                        ciselnik.IndexDodavatelu = new Dictionary<string, DetailNaradiDataDodavatel>();
                        foreach (var dodavatel in ciselnik.Dodavatele)
                            ciselnik.IndexDodavatelu[dodavatel.Kod] = dodavatel;
                    }

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
                    {
                        _cacheDodavatele.Insert("dodavatele", verze, ciselnik, dirty: true);

                        var kesovane = _cacheDetail.GetAllChanges();
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
                        _repository.NajitDetailyPodleDodavatele(novy.Kod, aktualizace.Execute, message.OnError);
                    }
                    else
                        message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistDodavatele(load.SetLoadedValue, load.LoadingFailed)
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
                    {
                        _cacheVady.Insert("vady", verze, ciselnik, dirty: true);

                        var kesovane = _cacheDetail.GetAllChanges();
                        foreach (var data in kesovane)
                        {
                            foreach (var naradi in data.IndexCislovane.Values)
                            {
                                if (naradi.NaVydejne != null && string.Equals(naradi.NaVydejne.KodVady, nova.Kod, StringComparison.Ordinal))
                                    naradi.NaVydejne.NazevVady = nova.Nazev;
                            }
                        }

                        var aktualizace = new AktualizaceReferenci(this, message.OnCompleted, message.OnError);
                        _repository.NajitDetailyPodleVady(nova.Kod, aktualizace.Execute, message.OnError);
                    }
                    else
                        message.OnCompleted();
                },
                message.OnError,
                load => _repository.NacistVady(load.SetLoadedValue, load.LoadingFailed));
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
                    {
                        _cachePracoviste.Insert(existujici.Kod, verze, existujici, dirty: true);

                        var kesovane = _cacheDetail.GetAllChanges();
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
                        _repository.NajitDetailyPodlePracoviste(nove.Kod, aktualizace.Execute, message.OnError);
                    }
                },
                message.OnError,
                load => _repository.NacistPracoviste(load.Key, load.SetLoadedValue, load.LoadingFailed));
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
                    _parent._cacheDetail.Get(
                        _kody[_pozice],
                        (verze, data) =>
                        {
                            var zmeneno = AktualizovatNaradi(data);
                            if (zmeneno)
                                _parent._cacheDetail.Insert(_kody[_pozice], verze, data, dirty: true);
                            Iterace();
                        }, _onError, _parent.NacistDataNaradi);
                }
            }

            private bool AktualizovatNaradi(DetailNaradiDataDetail data)
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

        private DetailNaradiNecislovane NajitNecislovane(DetailNaradiDataDetail data, UmisteniNaradiDto umisteni, DateTime terminDodani)
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

    public class DetailNaradiDataDetail
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

        public void NajitDetailyPodleDodavatele(string kodDodavatele, Action<IList<string>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("dodavatele", kodDodavatele, kodDodavatele, onNalezenSeznam, onError);
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

        public void NajitDetailyPodleVady(string kodVady, Action<IList<string>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("vady", kodVady, kodVady, onNalezenSeznam, onError);
        }

        public void NajitDetailyPodlePracoviste(string kodPracoviste, Action<IList<string>> onNalezenSeznam, Action<Exception> onError)
        {
            _folder.FindDocumentKeys("pracoviste", kodPracoviste, kodPracoviste, onNalezenSeznam, onError);
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

    public class DetailNaradiSerializer
    {
        private List<string> GenerovatReferenceDodavatelu(DetailNaradiDataDetail data)
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
        private List<string> GenerovatReferencePracovist(DetailNaradiDataDetail data)
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
        private List<string> GenerovatReferenceVad(DetailNaradiDataDetail data)
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
        public DetailNaradiDataDetail DeserializeNaradiForReader(string raw)
        {
            DetailNaradiDataDetail data;
            if (string.IsNullOrEmpty(raw))
                data = new DetailNaradiDataDetail();
            else
                data = JsonSerializer.DeserializeFromString<DetailNaradiDataDetail>(raw);

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
        public DetailNaradiDataDetail DeserializeNaradi(string raw)
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

        public IList<DocumentIndexing> IndexNaradi(DetailNaradiDataDetail data)
        {
            var indexy = new List<DocumentIndexing>();
            indexy.Add(new DocumentIndexing("vady", data.ReferenceVad));
            indexy.Add(new DocumentIndexing("pracoviste", data.ReferencePracovist));
            indexy.Add(new DocumentIndexing("dodavatele", data.ReferenceDodavatelu));
            return indexy;
        }
    }
}