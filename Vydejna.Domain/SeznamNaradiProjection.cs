using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Vydejna.Contracts;
using System.Xml.Linq;
using System.Xml;

namespace Vydejna.Domain
{
    public class SeznamNaradiProxy : ProjectionProxy<SeznamNaradiReader>, IReadSeznamNaradi
    {
        public SeznamNaradiProxy(IProjectionMetadataManager mgr)
            : base(mgr, "SeznamNaradi")
        {
        }

        public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            return Reader.NacistSeznamNaradi(offset, maxPocet);
        }

        public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            return Reader.OveritUnikatnost(vykres, rozmer);
        }
    }

    public class SeznamNaradiReader : IReadSeznamNaradi, IProjectionReader
    {
        private string _usedInstance = null;
        private Dictionary<string, IReadSeznamNaradi> _projections;
        private IReadSeznamNaradi _activeProjection;

        public SeznamNaradiReader()
        {
            _activeProjection = new SeznamNaradiDisabledProjection();
            _projections = new Dictionary<string, IReadSeznamNaradi>();
        }

        public void Register(string instanceName, IReadSeznamNaradi projection)
        {
            _projections[instanceName] = projection;
            if (string.Equals(_usedInstance, instanceName, StringComparison.Ordinal))
                _activeProjection = projection;
        }

        public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            return _activeProjection.NacistSeznamNaradi(offset, maxPocet);
        }

        public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            return _activeProjection.OveritUnikatnost(vykres, rozmer);
        }

        public string GetVersion()
        {
            return "1.0";
        }

        public string GetProjectionName()
        {
            return "SeznamNaradi";
        }

        public ProjectionReadability GetReadability(string minimalReaderVersion, string storedVersion)
        {
            return ProjectionUtils.CheckReaderVersion(minimalReaderVersion, GetVersion(), storedVersion, "0.0");
        }

        public void UseInstance(string instanceName)
        {
            _usedInstance = instanceName;
            if (instanceName == null)
                _activeProjection = new SeznamNaradiDisabledProjection();
            else if (!_projections.TryGetValue(instanceName, out _activeProjection))
                _activeProjection = new SeznamNaradiDisabledProjection();
        }

        private class SeznamNaradiDisabledProjection : IReadSeznamNaradi
        {
            public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
            {
                return TaskResult.GetFailedTask<SeznamNaradiDto>(new NotSupportedException("IReadSeznamNaradi.NacistSeznamNaradi has no handler"));
            }

            public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
            {
                return TaskResult.GetFailedTask<OvereniUnikatnostiDto>(new NotSupportedException("IReadSeznamNaradi.NacistSeznamNaradi has no handler"));
            }
        }
    }

    public class SeznamNaradiProjection : IReadSeznamNaradi, IProjection
        , IHandle<DefinovanoNaradiEvent>
        , IHandle<AktivovanoNaradiEvent>
        , IHandle<DeaktivovanoNaradiEvent>
    {
        private IDocumentStore _store;
        private string _documentBaseName;
        private string _documentFullName;
        private bool _dirty;
        private UpdateLock _lock = new UpdateLock();
        private List<Item> _data = new List<Item>();
        private HashSet<string> _existujici = new HashSet<string>();
        private Dictionary<Guid, Item> _indexId = new Dictionary<Guid, Item>();
        private ItemComparer _razeni = new ItemComparer();

        private class Item
        {
            public TypNaradiDto Dto;
        }

        public SeznamNaradiProjection(IDocumentStore store, string documentBaseName)
        {
            _store = store;
            _documentBaseName = documentBaseName;
        }

        private async Task NacistData()
        {
            var doc = await _store.GetDocument(_documentFullName);
            if (string.IsNullOrEmpty(doc))
                return;
            var xml = XDocument.Parse(doc);
            foreach (var xmlNaradi in xml.Element("SeznamNaradi").Elements("Naradi"))
            {
                var dto = new TypNaradiDto(
                    (Guid)xmlNaradi.Attribute("Id"),
                    (string)xmlNaradi.Attribute("Vykres"),
                    (string)xmlNaradi.Attribute("Rozmer"),
                    (string)xmlNaradi.Attribute("Druh"),
                    (bool)xmlNaradi.Attribute("Aktivni")
                    );
                var item = new Item { Dto = dto };
                _data.Add(item);
                _indexId[dto.Id] = item;
                _existujici.Add(KlicUnikatnosti(dto.Vykres, dto.Rozmer));
            }
            _data.Sort(_razeni);
        }

        private async Task UlozitData()
        {
            using (_lock.Update())
            {
                if (!_dirty)
                    return;
                var builder = new StringBuilder();
                using (var writer = XmlWriter.Create(new System.IO.StringWriter(builder)))
                {
                    writer.WriteStartElement("SeznamNaradi");
                    foreach (var item in _data)
                    {
                        var dto = item.Dto;
                        writer.WriteStartElement("Naradi");
                        writer.WriteAttributeString("Id", dto.Id.ToString());
                        writer.WriteAttributeString("Vykres", dto.Vykres);
                        writer.WriteAttributeString("Rozmer", dto.Rozmer);
                        writer.WriteAttributeString("Druh", dto.Druh);
                        writer.WriteAttributeString("Aktivni", dto.Aktivni ? "true" : "false");
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                await _store.SaveDocument(_documentFullName, builder.ToString());
                _dirty = false;
            }
        }

        public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            using (_lock.Read())
            {
                var filtrovano = _data.Skip(offset).Take(maxPocet).Select(i => i.Dto);
                var dto = new SeznamNaradiDto() { Offset = offset, PocetCelkem = _data.Count };
                dto.SeznamNaradi.AddRange(filtrovano);
                return TaskResult.GetCompletedTask(dto);
            }
        }

        public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            using (_lock.Read())
            {
                var existuje = _existujici.Contains(KlicUnikatnosti(vykres, rozmer));
                var dto = new OvereniUnikatnostiDto() { Vykres = vykres, Rozmer = rozmer, Existuje = existuje };
                return TaskResult.GetCompletedTask(dto);
            }
        }

        private static string KlicUnikatnosti(string vykres, string rozmer)
        {
            return string.Format("{0}:::{1}", vykres, rozmer);
        }

        public Task Handle(DefinovanoNaradiEvent message)
        {
            using (_lock.Update())
            {
                var dto = new TypNaradiDto(message.NaradiId, message.Vykres, message.Rozmer, message.Druh, true);
                var item = new Item { Dto = dto };
                int index = _data.BinarySearch(item, _razeni);
                if (index < 0)
                {
                    _lock.Write();
                    _data.Insert(~index, item);
                    _existujici.Add(KlicUnikatnosti(dto.Vykres, dto.Rozmer));
                    _indexId[dto.Id] = item;
                    _dirty = true;
                }
            }
            return TaskResult.GetCompletedTask();
        }

        public Task Handle(AktivovanoNaradiEvent message)
        {
            using (_lock.Update())
            {
                var item = _indexId[message.NaradiId];
                if (item.Dto.Aktivni)
                    return TaskResult.GetCompletedTask();
                var newDto = Clone(item.Dto);
                newDto.Aktivni = true;
                _lock.Write();
                item.Dto = newDto;
                _dirty = true;
                return TaskResult.GetCompletedTask();
            }
        }

        public Task Handle(DeaktivovanoNaradiEvent message)
        {
            using (_lock.Update())
            {
                var item = _indexId[message.NaradiId];
                if (!item.Dto.Aktivni)
                    return TaskResult.GetCompletedTask();
                var newDto = Clone(item.Dto);
                newDto.Aktivni = false;
                _lock.Write();
                item.Dto = newDto; 
                _dirty = true;
                return TaskResult.GetCompletedTask();
            }
        }

        private TypNaradiDto Clone(TypNaradiDto old)
        {
            return new TypNaradiDto(old.Id, old.Vykres, old.Rozmer, old.Druh, old.Aktivni);
        }

        private class ItemComparer : IComparer<Item>
        {
            public int Compare(Item x, Item y)
            {
                var comparer = StringComparer.OrdinalIgnoreCase;
                var vykres = comparer.Compare(x.Dto.Vykres, y.Dto.Vykres);
                if (vykres != 0)
                    return vykres;
                var rozmer = comparer.Compare(x.Dto.Rozmer, y.Dto.Rozmer);
                if (rozmer != 0)
                    return rozmer;
                return 0;
            }
        }

        private const string ProjectionVersion = "1.0";
        private IProjectionProcess _processServices;

        ProjectionRebuildType IProjection.NeedsRebuild(string storedVersion)
        {
            return ProjectionUtils.CheckWriterVersion(storedVersion, ProjectionVersion);
        }

        string IProjection.GetVersion()
        {
            return ProjectionVersion;
        }

        int IProjection.EventsBulkSize()
        {
            return 1000;
        }

        Task IProjection.StartRebuild(bool continuation)
        {
            if (!continuation)
            {
                _dirty = true;
                _data.Clear();
                _existujici.Clear();
                _indexId.Clear();
            }
            return TaskResult.GetCompletedTask();
        }

        Task IProjection.PartialCommit()
        {
            return UlozitData();
        }

        Task IProjection.CommitRebuild()
        {
            return UlozitData();
        }

        Task IProjection.StopRebuild()
        {
            return UlozitData();
        }

        bool IProjection.SupportsProcessServices()
        {
            return true;
        }

        void IProjection.SetProcessServices(IProjectionProcess process)
        {
            _processServices = process;
        }

        string IEventConsumer.GetConsumerName()
        {
            return "SeznamNaradi";
        }

        async Task IEventConsumer.HandleShutdown()
        {
            await UlozitData();
            await _processServices.CommitProjectionProgress();
        }

        string IProjection.GenerateInstanceName(string masterName)
        {
            if (string.IsNullOrEmpty(masterName) || masterName == "B")
                return "A";
            else
                return "B";
        }

        Task IProjection.SetInstanceName(string instanceName)
        {
            _documentFullName = _documentBaseName + instanceName;
            return NacistData();
        }

        public string GetMinimalReader()
        {
            return "1.0";
        }
    }
}
