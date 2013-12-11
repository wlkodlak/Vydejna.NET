using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiService
        : IWriteSeznamNaradi
        , IHandle<DefinovatNaradiInternalCommand>
        , IHandle<DokoncitDefiniciNaradiInternalCommand>
    {
        private INaradiRepository _repoNaradi;
        private IUnikatnostNaradiRepository _repoUnikatnost;

        public SeznamNaradiService(INaradiRepository repoNaradi, IUnikatnostNaradiRepository repoUnikatnost)
        {
            _repoNaradi = repoNaradi;
            _repoUnikatnost = repoUnikatnost;
        }

        public async Task Handle(AktivovatNaradiCommand message)
        {
            var naradi = await _repoNaradi.Get(message.NaradiId);
            if (naradi == null)
                return;
            naradi.Aktivovat();
            await _repoNaradi.Save(naradi);
        }

        public async Task Handle(DeaktivovatNaradiCommand message)
        {
            var naradi = await _repoNaradi.Get(message.NaradiId);
            if (naradi == null)
                return;
            naradi.Deaktivovat();
            await _repoNaradi.Save(naradi);
        }

        public async Task Handle(DefinovatNaradiInternalCommand message)
        {
            var naradi = await _repoNaradi.Get(message.NaradiId);
            if (naradi != null)
                return;
            naradi = Naradi.Definovat(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoNaradi.Save(naradi);
        }

        public async Task Handle(DefinovatNaradiCommand message)
        {
            var unikatnost = await _repoUnikatnost.Get();
            unikatnost.ZahajitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoUnikatnost.Save(unikatnost);
        }

        public async Task Handle(DokoncitDefiniciNaradiInternalCommand message)
        {
            var unikatnost = await _repoUnikatnost.Get();
            unikatnost.DokoncitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoUnikatnost.Save(unikatnost);
        }
    }
}
