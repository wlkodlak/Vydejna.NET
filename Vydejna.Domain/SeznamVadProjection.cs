using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamVadProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<DefinovanaVadaNaradiEvent>>
    {
        private IDocumentFolder _store;
        private SeznamVadData _data;
        private int _dataVersion;
        private SeznamVadDataSerializer _serializer;
        private SeznamVadKodComparer _comparer;

        public SeznamVadProjection(IDocumentFolder store)
        {
            _store = store;
            _serializer = new SeznamVadDataSerializer();
            _comparer = new SeznamVadKodComparer();
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
                "seznamvad", serialized, DocumentStoreVersion.At(_dataVersion), null,
                () => { _dataVersion++; message.OnCompleted(); },
                () => message.OnError(new ProjectorMessages.ConcurrencyException()),
                message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument(
                "seznamvad",
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

        public void Handle(CommandExecution<DefinovanaVadaNaradiEvent> message)
        {
            var vzor = new SeznamVadPolozka { Kod = message.Command.Kod };
            var index = _data.Seznam.BinarySearch(vzor, _comparer);
            if (index < 0)
            {
                var vada = new SeznamVadPolozka();
                vada.Kod = message.Command.Kod;
                vada.Nazev = message.Command.Nazev;
                vada.Aktivni = !message.Command.Deaktivovana;
                _data.Seznam.Insert(~index, vada);
            }
            else
            {
                var vada = _data.Seznam[index];
                vada.Nazev = message.Command.Nazev;
                vada.Aktivni = !message.Command.Deaktivovana;
            }
            message.OnCompleted();
        }
    }

    public class SeznamVadData
    {
        public List<SeznamVadPolozka> Seznam { get; set; }
    }
    public class SeznamVadKodComparer
        : IComparer<SeznamVadPolozka>
    {
        public int Compare(SeznamVadPolozka x, SeznamVadPolozka y)
        {
            return string.CompareOrdinal(x.Kod, y.Kod);
        }
    }

    public class SeznamVadDataSerializer
    {
        public SeznamVadData Deserialize(string raw)
        {
            var data = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamVadData>(raw);
            data = data ?? new SeznamVadData();
            data.Seznam = data.Seznam ?? new List<SeznamVadPolozka>();
            return data;
        }
        public string Serialize(SeznamVadData data)
        {
            return data == null ? "" : JsonSerializer.SerializeToString(data);
        }
    }
}
