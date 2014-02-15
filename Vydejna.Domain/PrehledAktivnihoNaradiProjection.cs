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
    public class PrehledAktivnihoNaradiProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.RebuildFinished>>
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
        , IHandle<CommandExecution<AktivovanoNaradiEvent>>
        , IHandle<CommandExecution<DeaktivovanoNaradiEvent>>
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
        private PrehledAktivnihoNaradiData _data;
        private PrehledAktivnihoNaradiDataComparer _comparer;
        private PrehledAktivnihoNaradiSerializer _serializer;
        private int _documentVersion;

        public PrehledAktivnihoNaradiProjection(IDocumentFolder store)
        {
            _store = store;
        }

        public string GetVersion()
        {
            return _version;
        }

        public EventProjectionUpgradeMode UpgradeMode(string storedVersion)
        {
            return (storedVersion == _version) ? EventProjectionUpgradeMode.NotNeeded : EventProjectionUpgradeMode.Rebuild;
        }

        public void Handle(CommandExecution<ProjectorMessages.Reset> message)
        {
            _store.DeleteAll(message.OnCompleted, message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.RebuildFinished> message)
        {
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument("all",
                (v, c) =>
                {
                    _documentVersion = v;
                    _data = _serializer.Deserialize(c);
                    message.OnCompleted();
                },
                () => {
                    _documentVersion = 0;
                    _data = _serializer.Deserialize(null);
                    message.OnCompleted();
                }, 
                message.OnError);
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> message)
        {
            PrehledAktivnihoNaradiDataNaradi naradi;
            if (!_data.IndexNaradiId.TryGetValue(message.Command.NaradiId, out naradi))
            {
                naradi = new PrehledAktivnihoNaradiDataNaradi
                {
                    NaradiId = message.Command.NaradiId,
                    Aktivni = true,
                    Vykres = message.Command.Vykres,
                    Rozmer = message.Command.Rozmer,
                    Druh = message.Command.Druh
                };
                _data.IndexNaradiId[message.Command.NaradiId] = naradi;
                var pozice = _data.Seznam.BinarySearch(naradi, _comparer);
                _data.Seznam.Insert((pozice >= 0) ? pozice : ~pozice, naradi);
            }
            message.OnCompleted();
        }

        public void Handle(CommandExecution<AktivovanoNaradiEvent> message)
        {
            PrehledAktivnihoNaradiDataNaradi naradi;
            if (_data.IndexNaradiId.TryGetValue(message.Command.NaradiId, out naradi))
            {
                naradi.Aktivni = true;
            }
            message.OnCompleted();
        }

        public void Handle(CommandExecution<DeaktivovanoNaradiEvent> message)
        {
            PrehledAktivnihoNaradiDataNaradi naradi;
            if (_data.IndexNaradiId.TryGetValue(message.Command.NaradiId, out naradi))
            {
                naradi.Aktivni = false;
            }
            message.OnCompleted();
        }

        private void PresunCislovanehoNaradi(Guid naradiId, int cisloNaradi, UmisteniNaradiDto puvodni, UmisteniNaradiDto nove, Action onCompleted)
        {
            PrehledAktivnihoNaradiDataNaradi naradi;
            if (_data.IndexNaradiId.TryGetValue(naradiId, out naradi))
            {
                if (puvodni != null)
                    SeznamCislovanehoNaradiNaUmisteni(naradi, puvodni).Remove(cisloNaradi);
                if (nove != null)
                    SeznamCislovanehoNaradiNaUmisteni(naradi, nove).Add(cisloNaradi);
            }
            onCompleted();
        }

        private HashSet<int> SeznamCislovanehoNaradiNaUmisteni(PrehledAktivnihoNaradiDataNaradi naradi, UmisteniNaradiDto umisteni)
        {
            if (umisteni == null)
                return new HashSet<int>();
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            return naradi.CislovaneVPoradku;
                        case "NutnoOpravit":
                            return naradi.CislovanePoskozene;
                        case "Neopravitelne":
                            return naradi.CislovaneZnicene;
                        default:
                            return new HashSet<int>();
                    }
                case ZakladUmisteni.VOprave:
                    return naradi.CislovaneOpravovane;
                case ZakladUmisteni.VeVyrobe:
                    return naradi.CislovaneVeVyrobe;
                default:
                    return new HashSet<int>();
            }
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, null, message.Command.NoveUmisteni, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKOpraveEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPrijatoZOpravyEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.OnCompleted);
        }

        public void Handle(CommandExecution<CislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            PresunCislovanehoNaradi(message.Command.NaradiId, message.Command.CisloNaradi, message.Command.PredchoziUmisteni, null, message.OnCompleted);
        }

        private void PresunNecislovanehoNaradi(Guid naradiId, Action onCompleted, UmisteniNaradiDto predchozi, UmisteniNaradiDto nove, int pocet)
        {
            PrehledAktivnihoNaradiDataNaradi naradi;
            if (_data.IndexNaradiId.TryGetValue(naradiId, out naradi))
            {
                UpravitPocetNaNecislovanemUmisteni(naradi, predchozi, -pocet);
                UpravitPocetNaNecislovanemUmisteni(naradi, nove, pocet);
            }
            onCompleted();
        }

        private static void UpravitPocetNaNecislovanemUmisteni(PrehledAktivnihoNaradiDataNaradi naradi, UmisteniNaradiDto umisteni, int zmenaPoctu)
        {
            if (umisteni == null)
                return;
            switch (umisteni.ZakladniUmisteni)
            {
                case ZakladUmisteni.NaVydejne:
                    switch (umisteni.UpresneniZakladu)
                    {
                        case "VPoradku":
                            naradi.NecislovaneVPoradku += zmenaPoctu;
                            break;
                        case "NutnoOpravit":
                            naradi.NecislovanePoskozene += zmenaPoctu;
                            break;
                        case "Neopravitelne":
                            naradi.NecislovaneZnicene += zmenaPoctu;
                            break;
                    }
                    break;
                case ZakladUmisteni.VeVyrobe:
                    naradi.NecislovaneVeVyrobe += zmenaPoctu;
                    break;
                case ZakladUmisteni.VOprave:
                    naradi.NecislovaneOpravovane += zmenaPoctu;
                    break;
            }
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoNaVydejnuEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, null, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiVydanoDoVyrobyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZVyrobyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKOpraveEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPrijatoZOpravyEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }

        public void Handle(CommandExecution<NecislovaneNaradiPredanoKeSesrotovaniEvent> message)
        {
            PresunNecislovanehoNaradi(message.Command.NaradiId, message.OnCompleted, message.Command.PredchoziUmisteni, message.Command.NoveUmisteni, message.Command.Pocet);
        }
    }

    public class PrehledAktivnihoNaradiSerializer
    {
        public PrehledAktivnihoNaradiData Deserialize(string contents)
        {
            PrehledAktivnihoNaradiData result;
            if (!string.IsNullOrEmpty(contents))
            {
                result = JsonSerializer.DeserializeFromString<PrehledAktivnihoNaradiData>(contents);
            }
            else
            {
                result = new PrehledAktivnihoNaradiData();
            }
            result.Seznam = result.Seznam ?? new List<PrehledAktivnihoNaradiDataNaradi>();
            result.IndexNaradiId = result.Seznam.ToDictionary(n => n.NaradiId);
            return result;
        }
        public string Serialize(PrehledAktivnihoNaradiData data)
        {
            var redukovanaKopie = new PrehledAktivnihoNaradiData { Seznam = data.Seznam };
            return JsonSerializer.SerializeToString(redukovanaKopie);
        }
    }
    public class PrehledAktivnihoNaradiData
    {
        public Dictionary<Guid, PrehledAktivnihoNaradiDataNaradi> IndexNaradiId;
        public List<PrehledAktivnihoNaradiDataNaradi> Seznam { get; set; }
    }
    public class PrehledAktivnihoNaradiDataNaradi
    {
        public Guid NaradiId { get; set; }
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
        public bool Aktivni { get; set; }
        public int NaSklade { get; set; }
        public int NecislovaneVPoradku { get; set; }
        public int NecislovaneVeVyrobe { get; set; }
        public int NecislovanePoskozene { get; set; }
        public int NecislovaneZnicene { get; set; }
        public int NecislovaneOpravovane { get; set; }
        public HashSet<int> CislovaneVPoradku { get; set; }
        public HashSet<int> CislovaneVeVyrobe { get; set; }
        public HashSet<int> CislovanePoskozene { get; set; }
        public HashSet<int> CislovaneZnicene { get; set; }
        public HashSet<int> CislovaneOpravovane { get; set; }
    }
    public class PrehledAktivnihoNaradiDataComparer : IComparer<PrehledAktivnihoNaradiDataNaradi>
    {
        public int Compare(PrehledAktivnihoNaradiDataNaradi x, PrehledAktivnihoNaradiDataNaradi y)
        {
            int result;
            result = string.CompareOrdinal(x.Vykres, y.Vykres);
            if (result != 0)
                return result;
            result = string.CompareOrdinal(x.Rozmer, y.Rozmer);
            if (result != 0)
                return result;
            return 0;
        }
    }

}
