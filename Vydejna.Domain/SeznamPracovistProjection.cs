using ServiceLib;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamPracovistProjection
        : IEventProjection
        , IHandle<CommandExecution<ProjectorMessages.Flush>>
        , IHandle<CommandExecution<ProjectorMessages.Resume>>
        , IHandle<CommandExecution<DefinovanoPracovisteEvent>>
    {
        private IDocumentFolder _store;
        private SeznamPracovistData _data;
        private int _dataVersion;
        private SeznamPracovistDataSerializer _serializer;
        private SeznamPracovistDataPracovisteKodComparer _comparer;

        public SeznamPracovistProjection(IDocumentFolder store)
        {
            _store = store;
            _serializer = new SeznamPracovistDataSerializer();
            _comparer = new SeznamPracovistDataPracovisteKodComparer();
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
                "seznampracovist", serialized, DocumentStoreVersion.At(_dataVersion), null,
                () => { _dataVersion++; message.OnCompleted(); },
                () => message.OnError(new ProjectorMessages.ConcurrencyException()),
                message.OnError);
        }

        public void Handle(CommandExecution<ProjectorMessages.Resume> message)
        {
            _store.GetDocument(
                "seznampracovist",
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

        public void Handle(CommandExecution<DefinovanoPracovisteEvent> message)
        {
            var vzor = new SeznamPracovistPolozka { Kod = message.Command.Kod };
            var index = _data.Seznam.BinarySearch(vzor, _comparer);
            if (index < 0)
            {
                var pracoviste = new SeznamPracovistPolozka();
                pracoviste.Kod = message.Command.Kod;
                pracoviste.Nazev = message.Command.Nazev;
                pracoviste.Stredisko = message.Command.Stredisko;
                pracoviste.Aktivni = !message.Command.Deaktivovano;
                _data.Seznam.Insert(~index, pracoviste);
            }
            else
            {
                var pracoviste = _data.Seznam[index];
                pracoviste.Nazev = message.Command.Nazev;
                pracoviste.Stredisko = message.Command.Stredisko;
                pracoviste.Aktivni = !message.Command.Deaktivovano;
            }
            message.OnCompleted();
        }
    }

    public class SeznamPracovistData
    {
        public List<SeznamPracovistPolozka> Seznam { get; set; }
    }
    public class SeznamPracovistDataPracovisteKodComparer
        : IComparer<SeznamPracovistPolozka>
    {
        public int Compare(SeznamPracovistPolozka x, SeznamPracovistPolozka y)
        {
            return string.CompareOrdinal(x.Kod, y.Kod);
        }
    }

    public class SeznamPracovistDataSerializer
    {
        public SeznamPracovistData Deserialize(string raw)
        {
            var data = string.IsNullOrEmpty(raw) ? null : JsonSerializer.DeserializeFromString<SeznamPracovistData>(raw);
            data = data ?? new SeznamPracovistData();
            data.Seznam = data.Seznam ?? new List<SeznamPracovistPolozka>();
            return data;
        }
        public string Serialize(SeznamPracovistData data)
        {
            return data == null ? "" : JsonSerializer.SerializeToString(data);
        }
    }
}
