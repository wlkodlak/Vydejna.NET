using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections.DetailNaradiReadModel
{
    public class DetailNaradiProjection
        : IEventProjection
        , ISubscribeToCommandManager
        , IProcess<ProjectorMessages.Flush>
        , IProcess<DefinovanoNaradiEvent>
        , IProcess<AktivovanoNaradiEvent>
        , IProcess<DeaktivovanoNaradiEvent>
        , IProcess<ZmenenStavNaSkladeEvent>
        , IProcess<DefinovanDodavatelEvent>
        , IProcess<DefinovanaVadaNaradiEvent>
        , IProcess<DefinovanoPracovisteEvent>
        , IProcess<CislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcess<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcess<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcess<CislovaneNaradiPredanoKOpraveEvent>
        , IProcess<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<CislovaneNaradiPredanoKeSesrotovaniEvent>
        , IProcess<NecislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcess<NecislovaneNaradiVydanoDoVyrobyEvent>
        , IProcess<NecislovaneNaradiPrijatoZVyrobyEvent>
        , IProcess<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcess<NecislovaneNaradiPrijatoZOpravyEvent>
        , IProcess<NecislovaneNaradiPredanoKeSesrotovaniEvent>
    {
        private const string _version = "0.01";
        private DetailNaradiRepository _repository;
        private MemoryCache<DetailNaradiDataDetail> _cacheDetail;
        private MemoryCache<DetailNaradiDataDodavatele> _cacheDodavatele;
        private MemoryCache<DetailNaradiDataVady> _cacheVady;
        private MemoryCache<DefinovanoPracovisteEvent> _cachePracoviste;

        public DetailNaradiProjection(DetailNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheDetail = new MemoryCache<DetailNaradiDataDetail>(time);
            _cacheDodavatele = new MemoryCache<DetailNaradiDataDodavatele>(time);
            _cachePracoviste = new MemoryCache<DefinovanoPracovisteEvent>(time);
            _cacheVady = new MemoryCache<DetailNaradiDataVady>(time);
        }

        public void Subscribe(ICommandSubscriptionManager mgr)
        {
            mgr.Register<ProjectorMessages.Flush>(this);
            mgr.Register<DefinovanoNaradiEvent>(this);
            mgr.Register<AktivovanoNaradiEvent>(this);
            mgr.Register<DeaktivovanoNaradiEvent>(this);
            mgr.Register<ZmenenStavNaSkladeEvent>(this);
            mgr.Register<DefinovanDodavatelEvent>(this);
            mgr.Register<DefinovanaVadaNaradiEvent>(this);
            mgr.Register<DefinovanoPracovisteEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoNaVydejnuEvent>(this);
            mgr.Register<CislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<CislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<CislovaneNaradiPredanoKeSesrotovaniEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoNaVydejnuEvent>(this);
            mgr.Register<NecislovaneNaradiVydanoDoVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZVyrobyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKOpraveEvent>(this);
            mgr.Register<NecislovaneNaradiPrijatoZOpravyEvent>(this);
            mgr.Register<NecislovaneNaradiPredanoKeSesrotovaniEvent>(this);
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

        public Task Handle(ProjectorMessages.Reset message)
        {
            _cacheDetail.Clear();
            _cacheDodavatele.Clear();
            _cachePracoviste.Clear();
            _cacheVady.Clear();
            return _repository.Reset();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom message)
        {
            throw new NotSupportedException();
        }

        public Task Handle(ProjectorMessages.Flush message)
        {
            return TaskUtils.FromEnumerable(FlushInternal()).GetTask();
        }

        private IEnumerable<Task> FlushInternal()
        {
            foreach (var detail in _cacheDetail.GetAllChanges())
            {
                var cislovaneKeSmazani = detail.IndexCislovane.Where(c => c.Value.ZakladUmisteni == ZakladUmisteni.VeSrotu).Select(c => c.Key).ToList();
                foreach (var cisloNaradi in cislovaneKeSmazani)
                    detail.IndexCislovane.Remove(cisloNaradi);
                var stavyKeSmazani = detail.IndexPodleStavu.Where(n => n.Value.Pocet == 0).Select(n => n.Key).ToList();
                foreach (var stav in stavyKeSmazani)
                    detail.IndexPodleStavu.Remove(stav);
                var pracovisteKeSmazani = detail.IndexPodlePracoviste.Where(n => n.Value.Pocet == 0).Select(n => n.Key).ToList();
                foreach (var stav in pracovisteKeSmazani)
                    detail.IndexPodlePracoviste.Remove(stav);
                var objednavkyKeSmazani = detail.IndexPodleObjednavky.Where(n => n.Value.Pocet == 0).Select(n => n.Key).ToList();
                foreach (var stav in objednavkyKeSmazani)
                    detail.IndexPodleObjednavky.Remove(stav);

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

            Task taskFlush;

            taskFlush = _cacheDodavatele.Flush(save => _repository.UlozitDodavatele(save.Version, save.Value));
            yield return taskFlush;
            taskFlush.Wait();

            taskFlush = _cachePracoviste.Flush(save => _repository.UlozitPracoviste(save.Key, save.Version, save.Value));
            yield return taskFlush;
            taskFlush.Wait();

            taskFlush = _cacheVady.Flush(save => _repository.UlozitVadu(save.Version, save.Value));
            yield return taskFlush;
            taskFlush.Wait();

            taskFlush = _cacheDetail.Flush(save => _repository.UlozitDetail(save.Value.NaradiId, save.Version, save.Value));
            yield return taskFlush;
            taskFlush.Wait();
        }

        private Task ZpracovatHoleNaradi(Guid naradiId, Action<DetailNaradiDataDetail> updateAction)
        {
            return _cacheDetail.Get(naradiId.ToString("N"), load => _repository.NacistDetail(naradiId, load.OldVersion)
                .Transform(RozsiritData)).ContinueWith(task =>
            {
                var data = task.Result.Value;
                var verze = task.Result.Version;
                updateAction(data);
                _cacheDetail.Insert(naradiId.ToString("N"), verze, data, dirty: true);
            });
        }

        public Task Handle(DefinovanoNaradiEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                data.NaradiId = evnt.NaradiId;
                data.Vykres = evnt.Vykres;
                data.Rozmer = evnt.Rozmer;
                data.Druh = evnt.Druh;
                data.Aktivni = true;
            });
        }

        public Task Handle(AktivovanoNaradiEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                data.Aktivni = true;
            });
        }

        public Task Handle(DeaktivovanoNaradiEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                data.Aktivni = true;
            });
        }

        public Task Handle(ZmenenStavNaSkladeEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                data.NaSklade = evnt.NovyStav;
            });
        }

        public Task Handle(DefinovanDodavatelEvent message)
        {
            return _cacheDodavatele.Get("dodavatele", load => _repository.NacistDodavatele()
                .Transform(RozsiritData)).ContinueWith(task =>
            {
                var ciselnik = task.Result.Value;
                var verze = task.Result.Version;

                DetailNaradiDataDodavatel existujici;
                var novy = message;
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
            });
        }

        public Task Handle(DefinovanaVadaNaradiEvent message)
        {
            return _cacheVady.Get("vady", load => _repository.NacistVady()
                .Transform(RozsiritData)).ContinueWith(task =>
            {
                var verze = task.Result.Version;
                var ciselnik = task.Result.Value;

                DefinovanaVadaNaradiEvent existujici;
                var nova = message;
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

                if (!ciselnik.IndexVad.TryGetValue(nova.Kod, out existujici))
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
            });
        }

        public Task Handle(DefinovanoPracovisteEvent message)
        {
            return _cachePracoviste.Get(message.Kod, load => _repository.NacistPracoviste(load.Key)).ContinueWith(task =>
            {
                var existujici = task.Result.Value;
                var verze = task.Result.Version;

                var nove = message;
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
            });
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
            detail.KodVady = kodVady ?? "";
            if (vada != null)
                detail.NazevVady = vada.Nazev;
            if (detail.NazevVady == null)
                detail.NazevVady = "";
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
            else
            {
                detail.NazevPracoviste = "";
                detail.StrediskoPracoviste = "";
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
            switch (umisteni.UpresneniZakladu)
            {
                case "Oprava":
                    detail.TypOpravy = TypOpravy.Oprava;
                    break;
                case "Reklamace":
                    detail.TypOpravy = TypOpravy.Reklamace;
                    break;
            }
            if (detail.KodDodavatele != null && dodavatel != null)
            {
                detail.NazevDodavatele = dodavatel.Nazev;
            }
            else
            {
                detail.NazevDodavatele = "";
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

        private IEnumerable<Task> ZpracovatCislovaneNaradi(
            Guid naradiId, int cisloNaradi,
            UmisteniNaradiDto predchoziUmisteni, UmisteniNaradiDto noveUmisteni,
            string kodVady = null, DateTime terminDodani = default(DateTime))
        {
            DefinovanaVadaNaradiEvent nactenaVada = null;
            DetailNaradiDataDodavatel nactenyDodavatel = null;
            DefinovanoPracovisteEvent nactenePracoviste = null;

            var kodPracoviste = UmisteniVeVyrobe(noveUmisteni) ? noveUmisteni.Pracoviste : null;
            var kodDodavatele = UmisteniVOprave(noveUmisteni) ? noveUmisteni.Dodavatel : null;

            var taskDetail = _cacheDetail.Get(naradiId.ToString("N"), load => _repository.NacistDetail(naradiId, load.OldVersion).Transform(RozsiritData));
            yield return taskDetail;
            var nactenyDetail = taskDetail.Result.Value;
            var verzeDetailu = taskDetail.Result.Version;

            if (kodVady != null)
            {
                var taskVady = _cacheVady.Get("vady", load => _repository.NacistVady().Transform(RozsiritData));
                yield return taskVady;
                var vady = taskVady.Result.Value;
                if (vady != null)
                    vady.IndexVad.TryGetValue(kodVady, out nactenaVada);
            }

            if (kodDodavatele != null)
            {
                var taskDodavatele = _cacheDodavatele.Get("dodavatele", load => _repository.NacistDodavatele().Transform(RozsiritData));
                yield return taskDodavatele;
                var dodavatele = taskDodavatele.Result.Value;
                if (dodavatele != null)
                    dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out nactenyDodavatel);
            }

            if (kodPracoviste != null)
            {
                var taskPracoviste = _cachePracoviste.Get(kodPracoviste, load => _repository.NacistPracoviste(kodPracoviste));
                yield return taskPracoviste;
                nactenePracoviste = taskPracoviste.Result.Value;
            }

            DetailNaradiCislovane cislovane;
            if (!nactenyDetail.IndexCislovane.TryGetValue(cisloNaradi, out cislovane))
                yield break;

            var puvodniVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
            var puvodniPracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
            var puvodniDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

            cislovane.ZakladUmisteni = noveUmisteni.ZakladniUmisteni;
            cislovane.NaVydejne = PrevodUmisteniNaVydejne(noveUmisteni, kodVady, nactenaVada);
            cislovane.VeVyrobe = PrevodUmisteniVeVyrobe(noveUmisteni, nactenePracoviste);
            cislovane.VOprave = PrevodUmisteniVOprave(noveUmisteni, terminDodani, nactenyDodavatel);

            var novaVada = (cislovane.NaVydejne != null) ? cislovane.NaVydejne.KodVady : null;
            var novePracoviste = (cislovane.VeVyrobe != null) ? cislovane.VeVyrobe.KodPracoviste : null;
            var novyDodavatel = (cislovane.VOprave != null) ? cislovane.VOprave.KodDodavatele : null;

            UpravitPocty(nactenyDetail.PoctyCelkem, predchoziUmisteni, -1);
            UpravitPocty(nactenyDetail.PoctyCelkem, noveUmisteni, 1);
            UpravitPocty(nactenyDetail.PoctyCislovane, predchoziUmisteni, -1);
            UpravitPocty(nactenyDetail.PoctyCislovane, noveUmisteni, 1);

            if (puvodniVada != novaVada)
                nactenyDetail.ReferenceVad = null;
            if (puvodniPracoviste != novePracoviste)
                nactenyDetail.ReferencePracovist = null;
            if (puvodniDodavatel != novyDodavatel)
                nactenyDetail.ReferenceDodavatelu = null;

            if (noveUmisteni.ZakladniUmisteni == ZakladUmisteni.VeSrotu)
            {
                nactenyDetail.IndexCislovane.Remove(cisloNaradi);
                nactenyDetail.Cislovane = null;
            }

            _cacheDetail.Insert(naradiId.ToString("N"), verzeDetailu, nactenyDetail, dirty: true);
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                var cislovane = new DetailNaradiCislovane();
                cislovane.CisloNaradi = evnt.CisloNaradi;
                cislovane.ZakladUmisteni = ZakladUmisteni.NaVydejne;
                cislovane.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null, null);
                data.Cislovane = null;
                data.IndexCislovane[evnt.CisloNaradi] = cislovane;
                data.PoctyCelkem.VPoradku += 1;
                data.PoctyCislovane.VPoradku += 1;
            });
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatCislovaneNaradi(evnt.NaradiId, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatCislovaneNaradi(evnt.NaradiId, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, kodVady: evnt.KodVady)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatCislovaneNaradi(evnt.NaradiId, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, terminDodani: evnt.TerminDodani)).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatCislovaneNaradi(evnt.NaradiId, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni)).GetTask();
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatCislovaneNaradi(evnt.NaradiId, evnt.CisloNaradi,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoNaVydejnuEvent evnt)
        {
            return ZpracovatHoleNaradi(evnt.NaradiId, data =>
            {
                DetailNaradiNecislovane naradi;
                if (!data.IndexPodleStavu.TryGetValue(StavNaradi.VPoradku, out naradi))
                {
                    naradi = new DetailNaradiNecislovane();
                    naradi.ZakladUmisteni = evnt.NoveUmisteni.ZakladniUmisteni;
                    naradi.NaVydejne = PrevodUmisteniNaVydejne(evnt.NoveUmisteni, null, null);
                    data.IndexPodleStavu[StavNaradi.VPoradku] = naradi;
                    data.Necislovane = null;
                }
                naradi.Pocet += evnt.Pocet;
                data.PoctyCelkem.VPoradku += evnt.Pocet;
                data.PoctyNecislovane.VPoradku += evnt.Pocet;
            });
        }

        public IEnumerable<Task> ZpracovatNecislovaneNaradi(
            Guid naradiId, UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, 
            int pocet, DateTime terminDodani = default(DateTime))
        {
            DetailNaradiDataDodavatel nactenyDodavatel = null;
            DefinovanoPracovisteEvent nactenePracoviste = null;

            var kodPracoviste = UmisteniVeVyrobe(nove) ? nove.Pracoviste : null;
            var kodDodavatele = UmisteniVOprave(nove) ? nove.Dodavatel : null;

            var taskDetail = _cacheDetail.Get(naradiId.ToString("N"), load => _repository.NacistDetail(naradiId, load.OldVersion).Transform(RozsiritData));
            yield return taskDetail;
            var nactenyDetail = taskDetail.Result.Value;
            var verzeDetailu = taskDetail.Result.Version;

            if (kodDodavatele != null)
            {
                var taskDodavatele = _cacheDodavatele.Get("dodavatele", load => _repository.NacistDodavatele().Transform(RozsiritData));
                yield return taskDodavatele;
                var dodavatele = taskDodavatele.Result.Value;
                if (dodavatele != null)
                    dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out nactenyDodavatel);
            }

            if (kodPracoviste != null)
            {
                var taskPracoviste = _cachePracoviste.Get(kodPracoviste, load => _repository.NacistPracoviste(kodPracoviste));
                yield return taskPracoviste;
                nactenePracoviste = taskPracoviste.Result.Value;
            }

            var proOdebrani = NajitNecislovane(nactenyDetail, predchozi, DateTime.MaxValue, false);
            var proPridani = NajitNecislovane(nactenyDetail, nove, terminDodani, true);

            if (proPridani != null)
            {
                if (proPridani.VeVyrobe != null && nactenePracoviste != null)
                {
                    proPridani.VeVyrobe.NazevPracoviste = nactenePracoviste.Nazev;
                    proPridani.VeVyrobe.StrediskoPracoviste = nactenePracoviste.Stredisko;
                }
                if (proPridani.VOprave != null && nactenyDodavatel != null)
                {
                    proPridani.VOprave.NazevDodavatele = nactenyDodavatel.Nazev;
                }
            }

            if (proOdebrani != null)
            {
                proOdebrani.Pocet -= pocet;
                if (proOdebrani.Pocet == 0)
                    nactenyDetail.Necislovane = null;
            }

            if (proPridani != null)
            {
                proPridani.Pocet += pocet;
                if (proPridani.Pocet == 0)
                    nactenyDetail.Necislovane = null;
                if (terminDodani != default(DateTime) && proPridani.VOprave != null)
                    proPridani.VOprave.TerminDodani = terminDodani;
            }

            UpravitPocty(nactenyDetail.PoctyCelkem, predchozi, -pocet);
            UpravitPocty(nactenyDetail.PoctyNecislovane, predchozi, -pocet);
            UpravitPocty(nactenyDetail.PoctyCelkem, nove, pocet);
            UpravitPocty(nactenyDetail.PoctyNecislovane, nove, pocet);

            _cacheDetail.Insert(naradiId.ToString("N"), verzeDetailu, nactenyDetail, dirty: true);
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

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatNecislovaneNaradi(evnt.NaradiId,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatNecislovaneNaradi(evnt.NaradiId,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatNecislovaneNaradi(evnt.NaradiId,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet, terminDodani: evnt.TerminDodani)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatNecislovaneNaradi(evnt.NaradiId,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet)).GetTask();
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent evnt)
        {
            return TaskUtils.FromEnumerable(ZpracovatNecislovaneNaradi(evnt.NaradiId,
                evnt.PredchoziUmisteni, evnt.NoveUmisteni, evnt.Pocet)).GetTask();
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

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        public Task<MemoryCacheItem<DetailNaradiDataDetail>> NacistDetail(Guid naradiId, int oldVersion)
        {
            return _folder.GetNewerDocument(string.Concat("detail-", naradiId.ToString("N")), oldVersion).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<DetailNaradiDataDetail>);
        }

        public Task<int> UlozitDetail(Guid naradiId, int verze, DetailNaradiDataDetail data)
        {
            return ProjectorUtils.Save(_folder, string.Concat("detail-", naradiId.ToString("N")), verze, JsonSerializer.SerializeToString(data), IndexyDetailu(data));
        }

        private IList<DocumentIndexing> IndexyDetailu(DetailNaradiDataDetail data)
        {
            var indexy = new List<DocumentIndexing>(4);
            indexy.Add(new DocumentIndexing("vady", data.ReferenceVad));
            indexy.Add(new DocumentIndexing("pracoviste", data.ReferencePracovist));
            indexy.Add(new DocumentIndexing("dodavatele", data.ReferenceDodavatelu));
            return indexy;
        }

        public Task<IList<Guid>> NajitDetailyPodleDodavatele(string kodDodavatele)
        {
            return _folder.FindDocumentKeys("dodavatele", kodDodavatele, kodDodavatele).ContinueWith(task => VytvoritSeznamNaradi(task.Result));
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

        public Task<MemoryCacheItem<DetailNaradiDataDodavatele>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<DetailNaradiDataDodavatele>);
        }

        public Task<int> UlozitDodavatele(int verze, DetailNaradiDataDodavatele ciselnik)
        {
            return ProjectorUtils.Save(_folder, "dodavatele", verze, JsonSerializer.SerializeToString(ciselnik), null);
        }

        public Task<MemoryCacheItem<DetailNaradiDataVady>> NacistVady()
        {
            return _folder.GetDocument("vady").ToMemoryCacheItem(JsonSerializer.DeserializeFromString<DetailNaradiDataVady>);
        }

        public Task<int> UlozitVadu(int verze, DetailNaradiDataVady ciselnik)
        {
            return ProjectorUtils.Save(_folder, "vady", verze, JsonSerializer.SerializeToString(ciselnik), null);
        }

        public Task<IList<Guid>> NajitDetailyPodleVady(string kodVady)
        {
            return _folder.FindDocumentKeys("vady", kodVady, kodVady).ContinueWith(task => VytvoritSeznamNaradi(task.Result));
        }

        public Task<IList<Guid>> NajitDetailyPodlePracoviste(string kodPracoviste)
        {
            return _folder.FindDocumentKeys("pracoviste", kodPracoviste, kodPracoviste).ContinueWith(task => VytvoritSeznamNaradi(task.Result));
        }

        public Task<MemoryCacheItem<DefinovanoPracovisteEvent>> NacistPracoviste(string kodPracoviste)
        {
            return _folder.GetDocument("pracoviste-" + kodPracoviste).ToMemoryCacheItem(JsonSerializer.DeserializeFromString<DefinovanoPracovisteEvent>);
        }

        public Task<int> UlozitPracoviste(string kodPracoviste, int verze, DefinovanoPracovisteEvent pracoviste)
        {
            return ProjectorUtils.Save(_folder, "pracoviste-" + kodPracoviste, verze, JsonSerializer.SerializeToString(pracoviste), null);
        }
    }

    public class DetailNaradiReader
        : IAnswer<DetailNaradiRequest, DetailNaradiResponse>
    {
        private DetailNaradiRepository _repository;
        private MemoryCache<DetailNaradiResponse> _cacheDetaily;

        public DetailNaradiReader(DetailNaradiRepository repository, ITime time)
        {
            _repository = repository;
            _cacheDetaily = new MemoryCache<DetailNaradiResponse>(time);
        }

        public void Subscribe(ISubscribable bus)
        {
            bus.Subscribe<DetailNaradiRequest, DetailNaradiResponse>(this);
        }

        public Task<DetailNaradiResponse> Handle(DetailNaradiRequest message)
        {
            return _cacheDetaily.Get(message.NaradiId.ToString("N"),
                load => _repository.NacistDetail(message.NaradiId, load.OldVersion)
                .Transform(data => VytvoritResponse(message, data))).ExtractValue();
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