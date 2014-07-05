using Npgsql;
using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Projections
{
    public class HistorieNaradiProjection
        : IEventProjection
        , ISubscribeToEventManager
        , IProcessEvent<DefinovanoNaradiEvent>
        , IProcessEvent<DefinovanDodavatelEvent>
        , IProcessEvent<DefinovanoPracovisteEvent>
        , IProcessEvent<DefinovanaVadaNaradiEvent>
        , IProcessEvent<CislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcessEvent<CislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<CislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<CislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<CislovaneNaradiPredanoKeSesrotovaniEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoNaVydejnuEvent>
        , IProcessEvent<NecislovaneNaradiVydanoDoVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZVyrobyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKOpraveEvent>
        , IProcessEvent<NecislovaneNaradiPrijatoZOpravyEvent>
        , IProcessEvent<NecislovaneNaradiPredanoKeSesrotovaniEvent>
    {
        private IHistorieNaradiRepositoryOperace _dbOperaci;
        private IHistorieNaradiRepositoryPomocne _dbPomocne;
        private MemoryCache<InformaceONaradi> _cacheNaradi;
        private MemoryCache<HistorieNaradiDataDodavatele> _cacheDodavatele;
        private MemoryCache<InformaceOPracovisti> _cachePracoviste;
        private MemoryCache<HistorieNaradiDataVady> _cacheVady;

        public HistorieNaradiProjection(
            IHistorieNaradiRepositoryOperace dbOperaci,
            IHistorieNaradiRepositoryPomocne dbPomocne,
            ITime time)
        {
            _dbOperaci = dbOperaci;
            _dbPomocne = dbPomocne;
            _cacheNaradi = new MemoryCache<InformaceONaradi>(time);
            _cacheDodavatele = new MemoryCache<HistorieNaradiDataDodavatele>(time);
            _cachePracoviste = new MemoryCache<InformaceOPracovisti>(time);
            _cacheVady = new MemoryCache<HistorieNaradiDataVady>(time);
        }

        public string GetVersion()
        {
            return "0.1";
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return string.Equals(storedVersion, GetVersion()) ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public Task Handle(ProjectorMessages.Reset command)
        {
            return TaskUtils.FromEnumerable(ResetInternal()).GetTask();
        }

        private IEnumerable<Task> ResetInternal()
        {
            _cacheDodavatele.Clear();
            _cacheNaradi.Clear();
            _cachePracoviste.Clear();
            _cacheVady.Clear();

            var taskOperace = _dbOperaci.Reset();
            yield return taskOperace;
            taskOperace.Wait();

            var taskPomocne = _dbPomocne.Reset();
            yield return taskPomocne;
            taskPomocne.Wait();
        }

        public Task Handle(ProjectorMessages.UpgradeFrom command)
        {
            throw new NotSupportedException();
        }

        public void Subscribe(IEventSubscriptionManager mgr)
        {
            mgr.Register<DefinovanoNaradiEvent>(this);
            mgr.Register<DefinovanDodavatelEvent>(this);
            mgr.Register<DefinovanoPracovisteEvent>(this);
            mgr.Register<DefinovanaVadaNaradiEvent>(this);
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

        public Task Handle(DefinovanoNaradiEvent command)
        {
            return TaskUtils.FromEnumerable(HandleInternal(command)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(DefinovanoNaradiEvent command)
        {
            var klicCache = command.NaradiId.ToString();
            var taskNacteni = _cacheNaradi.Get(klicCache, load => _dbPomocne.NacistNaradi(command.NaradiId));
            yield return taskNacteni;
            var verzeNaradi = taskNacteni.Result.Version;
            var naradi = taskNacteni.Result.Value;

            if (naradi == null)
            {
                naradi = new InformaceONaradi();
                naradi.NaradiId = command.NaradiId;
            }
            naradi.Vykres = command.Vykres;
            naradi.Rozmer = command.Rozmer;
            _cacheNaradi.Insert(klicCache, verzeNaradi, naradi);

            var taskUlozeni = _dbPomocne.UlozitNaradi(verzeNaradi, naradi);
            yield return taskUlozeni;
        }

        public Task Handle(DefinovanDodavatelEvent command)
        {
            return TaskUtils.FromEnumerable(HandleInternal(command)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(DefinovanDodavatelEvent command)
        {
            var taskNacteni = _cacheDodavatele.Get("dodavatele", load => _dbPomocne.NacistDodavatele().Transform(RozsiritData));
            yield return taskNacteni;
            var verzeDodavatele = taskNacteni.Result.Version;
            var dodavatele = taskNacteni.Result.Value;
            HistorieNaradiDataDodavatel dodavatel;
            if (!dodavatele.IndexDodavatelu.TryGetValue(command.Kod, out dodavatel))
                dodavatele.IndexDodavatelu[command.Kod] = dodavatel = new HistorieNaradiDataDodavatel();
            dodavatel.KodDodavatele = command.Kod;
            dodavatel.NazevDodavatele = command.Nazev;
            _cacheDodavatele.Insert("dodavatele", verzeDodavatele, dodavatele);

            var taskUlozeni = _dbPomocne.UlozitDodavatele(verzeDodavatele, dodavatele);
            yield return taskUlozeni;
        }

        private HistorieNaradiDataDodavatele RozsiritData(HistorieNaradiDataDodavatele zaklad)
        {
            zaklad = zaklad ?? new HistorieNaradiDataDodavatele();
            zaklad.Dodavatele = zaklad.Dodavatele ?? new List<HistorieNaradiDataDodavatel>();
            zaklad.IndexDodavatelu = new Dictionary<string, HistorieNaradiDataDodavatel>();
            foreach (var dodavatel in zaklad.Dodavatele)
                zaklad.IndexDodavatelu[dodavatel.KodDodavatele] = dodavatel;
            return zaklad;
        }

        public Task Handle(DefinovanoPracovisteEvent command)
        {
            return TaskUtils.FromEnumerable(HandleInternal(command)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(DefinovanoPracovisteEvent command)
        {
            var taskNacteni = _cachePracoviste.Get(command.Kod, load => _dbPomocne.NacistPracoviste(command.Kod));
            yield return taskNacteni;
            var verzePracoviste = taskNacteni.Result.Version;
            var pracoviste = taskNacteni.Result.Value;

            pracoviste = pracoviste ?? new InformaceOPracovisti();
            pracoviste.Kod = command.Kod;
            pracoviste.Nazev = command.Nazev;
            pracoviste.Stredisko = command.Stredisko;
            pracoviste.Aktivni = !command.Deaktivovano;
            _cachePracoviste.Insert(command.Kod, verzePracoviste, pracoviste);

            var taskUlozeni = _dbPomocne.UlozitPracoviste(verzePracoviste, pracoviste);
            yield return taskUlozeni;
        }

        public Task Handle(DefinovanaVadaNaradiEvent command)
        {
            return TaskUtils.FromEnumerable(HandleInternal(command)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(DefinovanaVadaNaradiEvent command)
        {
            var taskNacteni = _cacheVady.Get("vady", load => _dbPomocne.NacistVady());
            yield return taskNacteni;
            var verzeVad = taskNacteni.Result.Version;
            var vady = taskNacteni.Result.Value;

            DefinovanaVadaNaradiEvent vada;
            if (!vady.IndexVad.TryGetValue(command.Kod, out vada))
                vady.IndexVad[command.Kod] = vada = new DefinovanaVadaNaradiEvent();
            vada.Kod = command.Kod;
            vada.Nazev = command.Nazev;
            vada.Deaktivovana = command.Deaktivovana;
            _cacheVady.Insert("vady", verzeVad, vady);

            var taskUlozeni = _dbPomocne.UlozitVady(verzeVad, vady);
            yield return taskUlozeni;
        }

        private HistorieNaradiDataVady RozsiritData(HistorieNaradiDataVady zaklad)
        {
            zaklad = zaklad ?? new HistorieNaradiDataVady();
            zaklad.Vady = zaklad.Vady ?? new List<DefinovanaVadaNaradiEvent>();
            zaklad.IndexVad = new Dictionary<string, DefinovanaVadaNaradiEvent>();
            foreach (var dodavatel in zaklad.Vady)
                zaklad.IndexVad[dodavatel.Kod] = dodavatel;
            return zaklad;
        }

        private IEnumerable<Task> ZpracovatUdalost(
            Guid naradiId, 
            string kodDodavatele, 
            string kodPracoviste, 
            string kodVady,
            Action<HistorieNaradiOperace> update)
        {
            var operace = new HistorieNaradiOperace();
            {
                operace.NaradiId = naradiId;
                var taskNaradi = _cacheNaradi.Get(naradiId.ToString(), load => _dbPomocne.NacistNaradi(naradiId));
                yield return taskNaradi;
                var naradi = taskNaradi.Result.Value;
                operace.Vykres = naradi == null ? "" : naradi.Vykres;
                operace.Rozmer = naradi == null ? "" : naradi.Rozmer;
            }

            if (kodDodavatele != null)
            {
                operace.KodDodavatele = kodDodavatele;
                var taskDodavatele = _cacheDodavatele.Get("dodavatele", load => _dbPomocne.NacistDodavatele().Transform(RozsiritData));
                yield return taskDodavatele;
                var dodavatele = taskDodavatele.Result.Value;
                HistorieNaradiDataDodavatel dodavatel;
                if (dodavatele.IndexDodavatelu.TryGetValue(kodDodavatele, out dodavatel))
                    operace.NazevDodavatele = dodavatel.NazevDodavatele;
                else
                    operace.NazevDodavatele = "";
            }

            if (kodPracoviste != null)
            {
                operace.KodPracoviste = kodPracoviste;
                var taskPracoviste = _cachePracoviste.Get(kodPracoviste, load => _dbPomocne.NacistPracoviste(kodPracoviste));
                yield return taskPracoviste;
                var pracoviste = taskPracoviste.Result.Value;
                operace.NazevPracoviste = pracoviste == null ? "" : pracoviste.Nazev;
            }

            if (kodVady != null)
            {
                operace.KodVady = kodVady;
                var taskVady = _cacheVady.Get("vady", load => _dbPomocne.NacistVady().Transform(RozsiritData));
                yield return taskVady;
                var vady = taskVady.Result.Value;
                DefinovanaVadaNaradiEvent vada;
                if (vady.IndexVad.TryGetValue(kodVady, out vada))
                    operace.NazevVady = vada.Nazev;
                else
                    operace.NazevVady = "";
            }

            update(operace);

            var taskUlozeni = _dbOperaci.UlozitNovouOperaci(operace);
            yield return taskUlozeni;
        }

        private Task ZpracovatUdalost(
            CislovaneNaradiPresunutoEvent udalost,
            string kodDodavatele,
            string kodPracoviste,
            string kodVady,
            string nazevOperace,
            Action<HistorieNaradiOperace> update)
        {
            return TaskUtils.FromEnumerable(ZpracovatUdalost(udalost.NaradiId, kodDodavatele, kodPracoviste, kodVady, o =>
            {
                o.EventId = udalost.EventId;
                o.CisloNaradi = udalost.CisloNaradi;
                o.Datum = udalost.Datum;
                o.Pocet = 1;
                o.PuvodniCelkovaCena = udalost.CenaPredchozi;
                o.NovaCelkovaCena = udalost.CenaNova;
                o.TypUdalosti = udalost.GetType().Name;
                o.NazevOperace = nazevOperace;
                if (update != null)
                    update(o);
            })).GetTask();
        }

        private Task ZpracovatUdalost(
            NecislovaneNaradiPresunutoEvent udalost,
            string kodDodavatele,
            string kodPracoviste,
            string nazevOperace,
            Action<HistorieNaradiOperace> update)
        {
            return TaskUtils.FromEnumerable(ZpracovatUdalost(udalost.NaradiId, kodDodavatele, kodPracoviste, null, o =>
            {
                o.EventId = udalost.EventId;
                o.CisloNaradi = null;
                o.Datum = udalost.Datum;
                o.Pocet = udalost.Pocet;
                o.PuvodniCelkovaCena = udalost.CelkovaCenaPredchozi;
                o.NovaCelkovaCena = udalost.CelkovaCenaNova;
                o.TypUdalosti = udalost.GetType().Name;
                o.NazevOperace = nazevOperace;
                if (update != null)
                    update(o);
            })).GetTask();
        }

        public Task Handle(CislovaneNaradiPrijatoNaVydejnuEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, null, "Příjem na výdejnu", null);
        }

        public Task Handle(CislovaneNaradiVydanoDoVyrobyEvent command)
        {
            return ZpracovatUdalost(command, null, command.KodPracoviste, null, "Výdej na pracoviště", null);
        }

        public Task Handle(CislovaneNaradiPrijatoZVyrobyEvent command)
        {
            return ZpracovatUdalost(command, null, command.KodPracoviste, command.KodVady, "Příjem z výroby", o =>
            {
                o.StavNaradi = command.StavNaradi;
            });
        }

        public Task Handle(CislovaneNaradiPredanoKOpraveEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, null, "Výdej k opravě", o =>
                {
                    o.CisloObjednavky = command.Objednavka;
                });
        }

        public Task Handle(CislovaneNaradiPrijatoZOpravyEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, null, "Příjem z opravy", o =>
            {
                o.CisloObjednavky = command.Objednavka;
                o.StavNaradi = command.StavNaradi;
            });
        }

        public Task Handle(CislovaneNaradiPredanoKeSesrotovaniEvent command)
        {
            return ZpracovatUdalost(command, null, null, null, "Výdej do šrotu", null);
        }

        public Task Handle(NecislovaneNaradiPrijatoNaVydejnuEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, "Příjem na výdejnu", null);
        }

        public Task Handle(NecislovaneNaradiVydanoDoVyrobyEvent command)
        {
            return ZpracovatUdalost(command, null, command.KodPracoviste, "Výdej na pracoviště", null);
        }

        public Task Handle(NecislovaneNaradiPrijatoZVyrobyEvent command)
        {
            return ZpracovatUdalost(command, null, command.KodPracoviste, "Příjem z výroby", o =>
            {
                o.StavNaradi = command.StavNaradi;
            });
        }

        public Task Handle(NecislovaneNaradiPredanoKOpraveEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, "Výdej k opravě", o =>
            {
                o.CisloObjednavky = command.Objednavka;
            });
        }

        public Task Handle(NecislovaneNaradiPrijatoZOpravyEvent command)
        {
            return ZpracovatUdalost(command, command.KodDodavatele, null, "Příjem z opravy", o =>
            {
                o.CisloObjednavky = command.Objednavka;
                o.StavNaradi = command.StavNaradi;
            });
        }

        public Task Handle(NecislovaneNaradiPredanoKeSesrotovaniEvent command)
        {
            return ZpracovatUdalost(command, null, null, "Výdej do šrotu", null);
        }
    }

    public interface IHistorieNaradiRepositoryOperace
    {
        Task Reset();
        Task UlozitNovouOperaci(HistorieNaradiOperace operace);
        Task StornovatOperaci(Guid eventId);
        Task<Tuple<int, List<HistorieNaradiOperace>>> Najit(HistorieNaradiRequest filtr);
    }

    public class HistorieNaradiRepositoryOperace : IHistorieNaradiRepositoryOperace
    {
        private DatabasePostgres _db;

        public HistorieNaradiRepositoryOperace(DatabasePostgres db)
        {
            _db = db;
        }

        public Task Reset()
        {
            return _db.Execute(ResetInternal, null);
        }

        private void ResetInternal(NpgsqlConnection conn, object param)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DROP TABLE IF EXISTS historieoperaci;
                    CREATE TABLE historieoperaci (
	                    id varchar PRIMARY KEY,
	                    datum timestamp NOT NULL,
	                    cislonaradi integer,
	                    pocet integer NOT NULL,
	                    naradi varchar NOT NULL,
	                    vykres varchar,
	                    rozmer varchar,
	                    kodpracoviste varchar,
	                    nazevpracoviste varchar,
	                    koddodavatele varchar,
	                    nazevdodavatele varchar,
	                    objednavka varchar,
	                    typudalosti varchar,
	                    nazevoperace varchar,
	                    puvodnicena numeric,
	                    novacena numeric,
	                    stornovano boolean NOT NULL);
                    CREATE INDEX ON historieoperaci (datum);
                    CREATE INDEX ON historieoperaci (naradi, datum);
                    CREATE INDEX ON historieoperaci (cislonaradi, datum);
                    CREATE INDEX ON historieoperaci (kodpracoviste, datum);
                    CREATE INDEX ON historieoperaci (koddodavatele, objednavka, datum);
                    ";
                cmd.ExecuteNonQuery();
            }
        }

        public Task UlozitNovouOperaci(HistorieNaradiOperace operace)
        {
            return _db.Execute(UlozitNovouOperaciInternal, operace);
        }

        private void UlozitNovouOperaciInternal(NpgsqlConnection conn, object param)
        {
            try
            {
                var operace = (HistorieNaradiOperace)param;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO historieoperaci (" +
                        "id, datum, cislonaradi, pocet, naradi, vykres, rozmer, kodpracoviste, " +
                        "nazevpracoviste, koddodavatele, nazevdodavatele, objednavka, typudalosti, nazevoperace, " +
                        "puvodnicena, novacena, stornovano) VALUES (" +
                        ":id, :datum, :cislonaradi, :pocet, :naradi, :vykres, :rozmer, :kodpracoviste, " +
                        ":nazevpracoviste, :koddodavatele, :nazevdodavatele, :objednavka, :typudalosti, :nazevoperace, " +
                        ":puvodnicena, :novacena, :stornovano)";
                    cmd.Parameters.AddWithValue("id", operace.EventId.ToString("N"));
                    cmd.Parameters.AddWithValue("datum", operace.Datum);
                    cmd.Parameters.AddWithValue("cislonaradi", operace.CisloNaradi.HasValue ? operace.CisloNaradi.Value : (object)null);
                    cmd.Parameters.AddWithValue("pocet", operace.Pocet);
                    cmd.Parameters.AddWithValue("naradi", operace.NaradiId);
                    cmd.Parameters.AddWithValue("vykres", operace.Vykres);
                    cmd.Parameters.AddWithValue("rozmer", operace.Rozmer);
                    cmd.Parameters.AddWithValue("kodpracoviste", operace.KodPracoviste);
                    cmd.Parameters.AddWithValue("nazevpracoviste", operace.NazevPracoviste);
                    cmd.Parameters.AddWithValue("koddodavatele", operace.KodDodavatele);
                    cmd.Parameters.AddWithValue("nazevdodavatele", operace.NazevDodavatele);
                    cmd.Parameters.AddWithValue("objednavka", operace.CisloObjednavky);
                    cmd.Parameters.AddWithValue("typudalosti", operace.TypUdalosti);
                    cmd.Parameters.AddWithValue("nazevoperace", operace.NazevOperace);
                    cmd.Parameters.AddWithValue("puvodnicena", operace.PuvodniCelkovaCena.HasValue ? operace.PuvodniCelkovaCena.Value : (object)null);
                    cmd.Parameters.AddWithValue("novacena", operace.NovaCelkovaCena.HasValue ? operace.NovaCelkovaCena.Value : (object)null);
                    cmd.Parameters.AddWithValue("stornovano", operace.Stornovano);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (NpgsqlException exception)
            {
                if (exception.Code == "23505")
                    return;
                throw;
            }
        }

        public Task StornovatOperaci(Guid eventId)
        {
            return _db.Execute(StornovatOperaciInternal, eventId);
        }

        private void StornovatOperaciInternal(NpgsqlConnection conn, object eventId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE historieoperaci SET stornovano = true WHERE id = :id";
                cmd.Parameters.AddWithValue("id", eventId);
                cmd.ExecuteNonQuery();
            }
        }

        public Task<Tuple<int, List<HistorieNaradiOperace>>> Najit(HistorieNaradiRequest filtr)
        {
            return _db.Query(NajitInternal, filtr);
        }

        private Tuple<int, List<HistorieNaradiOperace>> NajitInternal(NpgsqlConnection conn, object param)
        {
            var filtr = (HistorieNaradiRequest)param;
            int celkovyPocet = 0;
            var list = new List<HistorieNaradiOperace>();
            using (var cmd = conn.CreateCommand())
            {
                var sb = new StringBuilder();
                sb.Append(
                    "SELECT id, datum, cislonaradi, pocet, naradi, vykres, rozmer, kodpracoviste, " +
                    "nazevpracoviste, koddodavatele, nazevdodavatele, objednavka, typudalosti, nazevoperace, " +
                    "puvodnicena, novacena, stornovano FROM historieoperaci " +
                    "WHERE datum >= :datumod AND datum <= :datumdo AND NOT stornovano");
                cmd.Parameters.AddWithValue("datumod", filtr.DatumOd);
                cmd.Parameters.AddWithValue("datumdo", filtr.DatumDo);
                switch (filtr.TypFiltru)
                {
                    case HistorieNaradiTypFiltru.CislovaneNaradi:
                        sb.Append(" AND cislonaradi = :cislonaradi");
                        cmd.Parameters.AddWithValue("cislonaradi", filtr.CisloNaradi.Value);
                        break;
                    case HistorieNaradiTypFiltru.Naradi:
                        sb.Append(" AND naradi = :naradi");
                        cmd.Parameters.AddWithValue("naradi", filtr.NaradiId.Value.ToString("N"));
                        break;
                    case HistorieNaradiTypFiltru.Objednavka:
                        sb.Append(" AND koddodavatele = :koddodavatele AND objednavka = :objednavka");
                        cmd.Parameters.AddWithValue("koddodavatele", filtr.KodDodavatele);
                        cmd.Parameters.AddWithValue("objednavka", filtr.CisloObjednavky);
                        break;
                    case HistorieNaradiTypFiltru.Pracoviste:
                        sb.Append(" AND kodpracoviste = :kodpracoviste");
                        cmd.Parameters.AddWithValue("kodpracoviste", filtr.KodPracoviste);
                        break;
                }
                sb.Append(" ORDER BY datum DESC OFFSET ").Append(filtr.Stranka * 100 - 100).Append(" LIMIT 100");
                cmd.CommandText = sb.ToString();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var operace = new HistorieNaradiOperace();
                        operace.EventId = new Guid(reader.GetString(0));
                        operace.Datum = reader.GetDateTime(1);
                        operace.CisloNaradi = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                        operace.Pocet = reader.GetInt32(3);
                        operace.NaradiId = new Guid(reader.GetString(4));
                        operace.Vykres = reader.IsDBNull(5) ? null : reader.GetString(5);
                        operace.Rozmer = reader.IsDBNull(6) ? null : reader.GetString(6);
                        operace.KodPracoviste = reader.IsDBNull(7) ? null : reader.GetString(7);
                        operace.NazevPracoviste = reader.IsDBNull(8) ? null : reader.GetString(8);
                        operace.KodDodavatele = reader.IsDBNull(9) ? null : reader.GetString(9);
                        operace.NazevDodavatele = reader.IsDBNull(10) ? null : reader.GetString(10);
                        operace.CisloObjednavky = reader.IsDBNull(11) ? null : reader.GetString(11);
                        operace.TypUdalosti = reader.IsDBNull(12) ? null : reader.GetString(12);
                        operace.NazevOperace = reader.IsDBNull(13) ? null : reader.GetString(13);
                        operace.PuvodniCelkovaCena = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14);
                        operace.NovaCelkovaCena = reader.IsDBNull(15) ? (decimal?)null : reader.GetDecimal(15);
                        operace.Stornovano = reader.GetBoolean(16);
                        list.Add(operace);
                    }
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                var sb = new StringBuilder();
                sb.Append(
                    "SELECT COUNT(*)::integer FROM historieoperaci " +
                    "WHERE datum >= :datumod AND datum <= :datumdo AND NOT stornovano");
                cmd.Parameters.AddWithValue("datumod", filtr.DatumOd);
                cmd.Parameters.AddWithValue("datumdo", filtr.DatumDo);
                switch (filtr.TypFiltru)
                {
                    case HistorieNaradiTypFiltru.CislovaneNaradi:
                        sb.Append(" AND cislonaradi = :cislonaradi");
                        cmd.Parameters.AddWithValue("cislonaradi", filtr.CisloNaradi.Value);
                        break;
                    case HistorieNaradiTypFiltru.Naradi:
                        sb.Append(" AND naradi = :naradi");
                        cmd.Parameters.AddWithValue("naradi", filtr.NaradiId.Value.ToString("N"));
                        break;
                    case HistorieNaradiTypFiltru.Objednavka:
                        sb.Append(" AND koddodavatele = :koddodavatele AND objednavka = :objednavka");
                        cmd.Parameters.AddWithValue("koddodavatele", filtr.KodDodavatele);
                        cmd.Parameters.AddWithValue("objednavka", filtr.CisloObjednavky);
                        break;
                    case HistorieNaradiTypFiltru.Pracoviste:
                        sb.Append(" AND kodpracoviste = :kodpracoviste");
                        cmd.Parameters.AddWithValue("kodpracoviste", filtr.KodPracoviste);
                        break;
                }
                cmd.CommandText = sb.ToString();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        celkovyPocet = reader.GetInt32(0);
                }
            }
            return Tuple.Create(celkovyPocet, list);
        }
    }

    public interface IHistorieNaradiRepositoryPomocne
    {
        Task Reset();
        Task<MemoryCacheItem<InformaceONaradi>> NacistNaradi(Guid naradiId);
        Task<int> UlozitNaradi(int verze, InformaceONaradi naradi);
        Task<MemoryCacheItem<HistorieNaradiDataDodavatele>> NacistDodavatele();
        Task<int> UlozitDodavatele(int verze, HistorieNaradiDataDodavatele dodavatele);
        Task<MemoryCacheItem<InformaceOPracovisti>> NacistPracoviste(string kodPracoviste);
        Task<int> UlozitPracoviste(int verze, InformaceOPracovisti pracoviste);
        Task<MemoryCacheItem<HistorieNaradiDataVady>> NacistVady();
        Task<int> UlozitVady(int verze, HistorieNaradiDataVady vady);
    }

    public class HistorieNaradiRepositoryPomocne : IHistorieNaradiRepositoryPomocne
    {
        private IDocumentFolder _folder;

        public HistorieNaradiRepositoryPomocne(IDocumentFolder folder)
        {
            _folder = folder;
        }

        public Task Reset()
        {
            return _folder.DeleteAll();
        }

        private static string DokumentNaradi(Guid naradiId)
        {
            return string.Concat("naradi-", naradiId.ToString("N"));
        }

        public Task<MemoryCacheItem<InformaceONaradi>> NacistNaradi(Guid naradiId)
        {
            return _folder
                .GetDocument(DokumentNaradi(naradiId))
                .ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceONaradi>);
        }

        public Task<int> UlozitNaradi(int verze, InformaceONaradi naradi)
        {
            return ProjectorUtils.Save(
                _folder, DokumentNaradi(naradi.NaradiId), verze, 
                JsonSerializer.SerializeToString(naradi), null);
        }

        public Task<MemoryCacheItem<HistorieNaradiDataDodavatele>> NacistDodavatele()
        {
            return _folder.GetDocument("dodavatele")
                .ToMemoryCacheItem(JsonSerializer.DeserializeFromString<HistorieNaradiDataDodavatele>);
        }

        public Task<int> UlozitDodavatele(int verze, HistorieNaradiDataDodavatele dodavatele)
        {
            return ProjectorUtils.Save(
                _folder, "dodavatele", verze, 
                JsonSerializer.SerializeToString(dodavatele), null);
        }

        private static string DokumentPracoviste(string kodPracoviste)
        {
            return string.Concat("pracoviste-", kodPracoviste);
        }

        public Task<MemoryCacheItem<InformaceOPracovisti>> NacistPracoviste(string kodPracoviste)
        {
            return _folder.GetDocument(DokumentPracoviste(kodPracoviste))
                .ToMemoryCacheItem(JsonSerializer.DeserializeFromString<InformaceOPracovisti>);
        }

        public Task<int> UlozitPracoviste(int verze, InformaceOPracovisti pracoviste)
        {
            return ProjectorUtils.Save(
                _folder, DokumentPracoviste(pracoviste.Kod), verze,
                JsonSerializer.SerializeToString(pracoviste), null);
        }

        public Task<MemoryCacheItem<HistorieNaradiDataVady>> NacistVady()
        {
            return _folder.GetDocument("vady")
                .ToMemoryCacheItem(JsonSerializer.DeserializeFromString<HistorieNaradiDataVady>);
        }

        public Task<int> UlozitVady(int verze, HistorieNaradiDataVady vady)
        {
            return ProjectorUtils.Save(
                _folder, "vady", verze,
                JsonSerializer.SerializeToString(vady), null);
        }
    }

    public class HistorieNaradiDataDodavatel
    {
        public string KodDodavatele { get; set; }
        public string NazevDodavatele { get; set; }
    }
    public class HistorieNaradiDataVady
    {
        public List<DefinovanaVadaNaradiEvent> Vady { get; set; }
        public Dictionary<string, DefinovanaVadaNaradiEvent> IndexVad;
    }
    public class HistorieNaradiDataDodavatele
    {
        public List<HistorieNaradiDataDodavatel> Dodavatele { get; set; }
        public Dictionary<string, HistorieNaradiDataDodavatel> IndexDodavatelu;
    }

    public class HistorieNaradiReader
        : IAnswer<HistorieNaradiRequest, HistorieNaradiResponse>
    {
        private IHistorieNaradiRepositoryOperace _repository;

        public HistorieNaradiReader(IHistorieNaradiRepositoryOperace repository)
        {
            _repository = repository;
        }

        public Task<HistorieNaradiResponse> Handle(HistorieNaradiRequest query)
        {
            return TaskUtils.FromEnumerable<HistorieNaradiResponse>(HandleInternal(query)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(HistorieNaradiRequest query)
        {
            var taskHledani = _repository.Najit(query);
            yield return taskHledani;

            var response = new HistorieNaradiResponse();
            response.Filtr = query;
            response.PocetCelkem = taskHledani.Result.Item1;
            response.PocetStranek = (response.PocetCelkem + 99) / 100;
            response.SeznamOperaci = taskHledani.Result.Item2;
            yield return TaskUtils.FromResult(response);
        }
    }
}
