using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class SeznamNaradiService
        : IHandle<AktivovatNaradiCommand>
        , IHandle<DeaktivovatNaradiCommand>
        , IHandle<DefinovatNaradiCommand>
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

        public void Handle(AktivovatNaradiCommand message)
        {
            var naradi = _repoNaradi.Get(message.NaradiId);
            if (naradi == null)
                return;
            naradi.Aktivovat();
            _repoNaradi.Save(naradi);
        }

        public void Handle(DeaktivovatNaradiCommand message)
        {
            var naradi = _repoNaradi.Get(message.NaradiId);
            if (naradi == null)
                return;
            naradi.Deaktivovat();
            _repoNaradi.Save(naradi);
        }

        public void Handle(DefinovatNaradiInternalCommand message)
        {
            var naradi = _repoNaradi.Get(message.NaradiId);
            if (naradi != null)
                return;
            naradi = Naradi.Definovat(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            _repoNaradi.Save(naradi);
        }

        public void Handle(DefinovatNaradiCommand message)
        {
            var unikatnost = _repoUnikatnost.Get();
            unikatnost.ZahajitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            _repoUnikatnost.Save(unikatnost);
        }

        public void Handle(DokoncitDefiniciNaradiInternalCommand message)
        {
            var unikatnost = _repoUnikatnost.Get();
            unikatnost.DokoncitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            _repoUnikatnost.Save(unikatnost);
        }
    }
}
