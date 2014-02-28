using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamDodavateluProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<DefinovanDodavatelEvent>>
    {
        private IDocumentFolder _store;
        private SeznamDodavateluData _data;
        private int _dataVersion;
        private SeznamDodavateluDataSerializer _serializer;
        private SeznamDodavateluNazevComparer _comparer;

        public SeznamDodavateluProjection(IDocumentFolder store)
        {
            _store = store;
            _comparer = new SeznamDodavateluNazevComparer();
            _serializer = new SeznamDodavateluDataSerializer(_comparer);
            _data = null;
            _dataVersion = 0;
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
            _store.DeleteAll(message.OnCompleted, message.OnError);
            _data = _serializer.Deserialize(null);
            _dataVersion = 0;
        }

        public void Handle(CommandExecution<ProjectorMessages.UpgradeFrom> message)
        {
            throw new NotSupportedException();
        }

        public void Handle(CommandExecution<ProjectorMessages.Flush> message)
        {
            var serialized = _serializer.Serialize(_data);
            _store.SaveDocument(
                "seznamdodavatelu", serialized, DocumentStoreVersion.At(_dataVersion), null,
                () => { _dataVersion++; message.OnCompleted(); },
                () => message.OnError(new ProjectorMessages.ConcurrencyException()),
                message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument(
                "seznamdodavatelu",
                (version, content) =>
                {
                    _dataVersion = version;
                    _data = _serializer.Deserialize(content);
                    message.OnCompleted();
                },
                () =>
                {
                    _dataVersion = 0;
                    _data = _serializer.Deserialize(null);
                    message.OnCompleted();
                },
                message.OnError);
        }

        public void Handle(CommandExecution<DefinovanDodavatelEvent> message)
        {
            InformaceODodavateli dodavatel;
            if (!_data.PodleKodu.TryGetValue(message.Command.Kod, out dodavatel))
            {
                dodavatel = new InformaceODodavateli();
                dodavatel.Kod = message.Command.Kod;
                _data.PodleKodu[dodavatel.Kod] = dodavatel;
            }
            dodavatel.Nazev = message.Command.Nazev;
            dodavatel.Adresa = message.Command.Adresa;
            dodavatel.Ico = message.Command.Ico;
            dodavatel.Dic = message.Command.Dic;
            dodavatel.Aktivni = !message.Command.Deaktivovan;
            message.OnCompleted();
        }
    }

    public class SeznamDodavateluData
    {
        public List<InformaceODodavateli> Seznam { get; set; }
        public Dictionary<string, InformaceODodavateli> PodleKodu;
    }
    public class SeznamDodavateluNazevComparer
        : IComparer<InformaceODodavateli>
    {
        public int Compare(InformaceODodavateli x, InformaceODodavateli y)
        {
            return string.Compare(x.Nazev, y.Nazev);
        }
    }

    public class SeznamDodavateluDataSerializer
    {
        private IComparer<InformaceODodavateli> _comparer;

        public SeznamDodavateluDataSerializer(IComparer<InformaceODodavateli> comparer)
        {
            _comparer = comparer;
        }
        public SeznamDodavateluData Deserialize(string raw)
        {
            var data = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamDodavateluData>(raw);
            data = data ?? new SeznamDodavateluData();
            data.Seznam = data.Seznam ?? new List<InformaceODodavateli>();
            data.PodleKodu = new Dictionary<string, InformaceODodavateli>(data.Seznam.Count);
            foreach (var dodavatel in data.Seznam)
                data.PodleKodu[dodavatel.Kod] = dodavatel;
            return data;
        }
        public string Serialize(SeznamDodavateluData data)
        {
            if (data == null)
                return "";
            data.Seznam = new List<InformaceODodavateli>(data.PodleKodu.Values);
            data.Seznam.Sort(_comparer);
            return JsonSerializer.SerializeToString(data);
        }
    }

    public class SeznamDodavateluReader
        : IAnswer<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>
    {
        private IMemoryCache<ZiskatSeznamDodavateluResponse> _cache;
        private IDocumentFolder _store;
        private SeznamDodavateluDataSerializer _serializer;

        public SeznamDodavateluReader(IDocumentFolder store, IQueueExecution executor, ITime time)
        {
            _store = store;
            _cache = new MemoryCache<ZiskatSeznamDodavateluResponse>(executor, time);
            _serializer = new SeznamDodavateluDataSerializer(null);
        }

        public void Handle(QueryExecution<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse> message)
        {
            _cache.Get("all", (verze, data) => message.OnCompleted(data), message.OnError, NacistDodavatele);
        }

        private void NacistDodavatele(IMemoryCacheLoad<ZiskatSeznamDodavateluResponse> load)
        {
            if (load.OldValueAvailable)
            {
                _store.GetNewerDocument("seznamdodavatelu", load.OldVersion,
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponse(data)),
                    () => load.ValueIsStillValid(),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    load.LoadingFailed);
            }
            else
            {
                _store.GetDocument("seznamdodavatelu",
                    (verze, data) => load.SetLoadedValue(verze, VytvoritResponse(data)),
                    () => load.SetLoadedValue(0, VytvoritResponse(null)),
                    load.LoadingFailed);
            }
        }

        private ZiskatSeznamDodavateluResponse VytvoritResponse(string data)
        {
            var zaklad = _serializer.Deserialize(data);
            return new ZiskatSeznamDodavateluResponse { Seznam = zaklad.Seznam };
        }
    }
}
