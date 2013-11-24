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
    public class SeznamNaradiProxy : IReadSeznamNaradi
    {
        private SeznamNaradiProjection _instance;
        public SeznamNaradiProxy(SeznamNaradiProjection instance)
        {
            _instance = instance;
            instance.AssignProxy(this);
        }

        public void ReassignInstance(SeznamNaradiProjection instance)
        {
            _instance = instance;
        }

        public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            return _instance.NacistSeznamNaradi(offset, maxPocet);
        }

        public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            return _instance.OveritUnikatnost(vykres, rozmer);
        }
    }

    public class SeznamNaradiProjection : IReadSeznamNaradi
        , IHandle<DefinovanoNaradiEvent>
        , IHandle<AktivovanoNaradiEvent>
        , IHandle<DeaktivovanoNaradiEvent>
    {
        private IDocumentStore _store;
        private string _documentName;
        private bool _dirty;
        private UpdateLock _lock = new UpdateLock();
        private List<Item> _data = new List<Item>();
        private HashSet<string> _existujici = new HashSet<string>();
        private Dictionary<Guid, Item> _indexId = new Dictionary<Guid, Item>();
        private ItemComparer _razeni = new ItemComparer();
        private SeznamNaradiProxy _proxy;

        private class Item
        {
            public TypNaradiDto Dto;
        }

        public SeznamNaradiProjection(IDocumentStore store, string documentName)
        {
            _store = store;
            _documentName = documentName;
            NacistData().GetAwaiter().GetResult();
        }

        public void AssignProxy(SeznamNaradiProxy proxy)
        {
            _proxy = proxy;
        }

        private async Task NacistData()
        {
            var doc = await _store.GetDocument(_documentName);
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
                await _store.SaveDocument(_documentName, builder.ToString());
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

        public void Handle(DefinovanoNaradiEvent message)
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
        }

        public void Handle(AktivovanoNaradiEvent message)
        {
            using (_lock.Update())
            {
                var item = _indexId[message.NaradiId];
                if (item.Dto.Aktivni)
                    return;
                var newDto = Clone(item.Dto);
                newDto.Aktivni = true;
                _lock.Write();
                item.Dto = newDto;
                _dirty = true;
            }
        }

        public void Handle(DeaktivovanoNaradiEvent message)
        {
            using (_lock.Update())
            {
                var item = _indexId[message.NaradiId];
                if (!item.Dto.Aktivni)
                    return;
                var newDto = Clone(item.Dto);
                newDto.Aktivni = false;
                _lock.Write();
                item.Dto = newDto; 
                _dirty = true;
            }
        }

        public Task HandleShutdown()
        {
            return UlozitData();
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
    }
}
