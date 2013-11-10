using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiProjection : IReadSeznamNaradi
        , IHandle<DefinovanoNaradiEvent>
        , IHandle<AktivovanoNaradiEvent>
        , IHandle<DeaktivovanoNaradiEvent>
    {
        private List<TypNaradiDto> _data = new List<TypNaradiDto>();
        private HashSet<string> _existujici = new HashSet<string>();
        private Dictionary<Guid, TypNaradiDto> _indexId = new Dictionary<Guid, TypNaradiDto>();

        public Task<SeznamNaradiDto> NacistSeznamNaradi(int offset, int maxPocet)
        {
            var filtrovano = _data.Skip(offset).Take(maxPocet);
            var dto = new SeznamNaradiDto() { Offset = offset, PocetCelkem = _data.Count };
            dto.SeznamNaradi.AddRange(filtrovano);
            var task = new TaskCompletionSource<SeznamNaradiDto>();
            task.SetResult(dto);
            return task.Task;
        }

        public Task<OvereniUnikatnostiDto> OveritUnikatnost(string vykres, string rozmer)
        {
            var existuje = _existujici.Contains(KlicUnikatnosti(vykres, rozmer));
            var dto = new OvereniUnikatnostiDto() { Vykres = vykres, Rozmer = rozmer, Existuje = existuje };
            var task = new TaskCompletionSource<OvereniUnikatnostiDto>();
            task.SetResult(dto);
            return task.Task;
        }

        private string KlicUnikatnosti(string vykres, string rozmer)
        {
            return string.Format("{0}:::{1}", vykres, rozmer);
        }

        public void Handle(DefinovanoNaradiEvent message)
        {
            var dto = new TypNaradiDto(message.NaradiId, message.Vykres, message.Rozmer, message.Druh, true);
            PridatNaradi(new[] { dto });
        }

        public void Handle(AktivovanoNaradiEvent message)
        {
            _indexId[message.NaradiId].Aktivni = true;
        }

        public void Handle(DeaktivovanoNaradiEvent message)
        {
            _indexId[message.NaradiId].Aktivni = false;
        }

        public void Clear()
        {
            _data.Clear();
        }

        public void PridatNaradi(IList<TypNaradiDto> dtos)
        {
            _data.AddRange(dtos);
            _data.Sort(Razeni);
            foreach (var dto in dtos)
            {
                _existujici.Add(KlicUnikatnosti(dto.Vykres, dto.Rozmer));
                _indexId[dto.Id] = dto;
            }
        }

        private int Razeni(TypNaradiDto x, TypNaradiDto y)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var vykres = comparer.Compare(x.Vykres, y.Vykres);
            if (vykres != 0)
                return vykres;
            var rozmer = comparer.Compare(x.Rozmer, y.Rozmer);
            if (rozmer != 0)
                return rozmer;
            return 0;
        }

        public IList<TypNaradiDto> ZiskatVsechno()
        {
            return _data;
        }
    }
}
