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
        private log4net.ILog _log;
        private INaradiRepository _repoNaradi;
        private IUnikatnostNaradiRepository _repoUnikatnost;

        public SeznamNaradiService(INaradiRepository repoNaradi, IUnikatnostNaradiRepository repoUnikatnost)
        {
            _log = log4net.LogManager.GetLogger(typeof(SeznamNaradiService));
            _repoNaradi = repoNaradi;
            _repoUnikatnost = repoUnikatnost;
        }

        public async Task Handle(AktivovatNaradiCommand message)
        {
            _log.DebugFormat("AktivovatNaradi: {0}", message.NaradiId);
            var naradi = await _repoNaradi.Get(message.NaradiId).ConfigureAwait(false);
            if (naradi == null)
                return;
            naradi.Aktivovat();
            await _repoNaradi.Save(naradi).ConfigureAwait(false);
        }

        public async Task Handle(DeaktivovatNaradiCommand message)
        {
            _log.DebugFormat("DeaktivovatNaradi: {0}", message.NaradiId);
            var naradi = await _repoNaradi.Get(message.NaradiId).ConfigureAwait(false);
            if (naradi == null)
                return;
            naradi.Deaktivovat();
            await _repoNaradi.Save(naradi).ConfigureAwait(false);
        }

        public async Task Handle(DefinovatNaradiInternalCommand message)
        {
            _log.DebugFormat("DefinovatNaradiInternal: {0}, vykres {1}, rozmer {2}, druh {3}", 
                message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            var naradi = await _repoNaradi.Get(message.NaradiId).ConfigureAwait(false);
            if (naradi != null)
                return;
            naradi = Naradi.Definovat(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoNaradi.Save(naradi).ConfigureAwait(false);
        }

        public async Task Handle(DefinovatNaradiCommand message)
        {
            _log.DebugFormat("DefinovatNaradi: {0}, vykres {1}, rozmer {2}, druh {3}", 
                message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            var unikatnost = await _repoUnikatnost.Get().ConfigureAwait(false);
            if (unikatnost == null)
                unikatnost = new UnikatnostNaradi();
            unikatnost.ZahajitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoUnikatnost.Save(unikatnost).ConfigureAwait(false);
        }

        public async Task Handle(DokoncitDefiniciNaradiInternalCommand message)
        {
            _log.DebugFormat("DokoncitDefiniciNaradiInternal: {0}, vykres {1}, rozmer {2}, druh {3}", 
                message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            var unikatnost = await _repoUnikatnost.Get().ConfigureAwait(false);
            unikatnost.DokoncitDefinici(message.NaradiId, message.Vykres, message.Rozmer, message.Druh);
            await _repoUnikatnost.Save(unikatnost).ConfigureAwait(false);
        }
    }
}
