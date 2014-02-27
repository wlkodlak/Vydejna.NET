﻿using ServiceLib;
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
            SeznamDodavateluPolozka dodavatel;
            if (!_data.PodleKodu.TryGetValue(message.Command.Kod, out dodavatel))
            {
                dodavatel = new SeznamDodavateluPolozka();
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
        public List<SeznamDodavateluPolozka> Seznam { get; set; }
        public Dictionary<string, SeznamDodavateluPolozka> PodleKodu;
    }
    public class SeznamDodavateluNazevComparer
        : IComparer<SeznamDodavateluPolozka>
    {
        public int Compare(SeznamDodavateluPolozka x, SeznamDodavateluPolozka y)
        {
            return string.Compare(x.Nazev, y.Nazev);
        }
    }

    public class SeznamDodavateluDataSerializer
    {
        private IComparer<SeznamDodavateluPolozka> _comparer;

        public SeznamDodavateluDataSerializer(IComparer<SeznamDodavateluPolozka> comparer)
        {
            _comparer = comparer;
        }
        public SeznamDodavateluData Deserialize(string raw)
        {
            var data = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamDodavateluData>(raw);
            data = data ?? new SeznamDodavateluData();
            data.Seznam = data.Seznam ?? new List<SeznamDodavateluPolozka>();
            data.PodleKodu = new Dictionary<string, SeznamDodavateluPolozka>(data.Seznam.Count);
            foreach (var dodavatel in data.Seznam)
                data.PodleKodu[dodavatel.Kod] = dodavatel;
            return data;
        }
        public string Serialize(SeznamDodavateluData data)
        {
            if (data == null)
                return "";
            data.Seznam = new List<SeznamDodavateluPolozka>(data.PodleKodu.Values);
            data.Seznam.Sort(_comparer);
            return JsonSerializer.SerializeToString(data);
        }
    }
}