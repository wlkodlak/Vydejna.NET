using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class ProcesDefiniceNaradi
        : IHandle<ZahajenaDefiniceNaradiEvent>
        , IHandle<ZahajenaAktivaceNaradiEvent>
        , IHandle<DefinovanoNaradiEvent>
    {
        private IHandle<DefinovatNaradiCommand> _definice;
        private IHandle<AktivovatNaradiCommand> _aktivace;
        private IHandle<DokoncitDefiniciNaradiInternalCommand> _dokonceni;

        public ProcesDefiniceNaradi(
            IHandle<DefinovatNaradiCommand> definice,
            IHandle<AktivovatNaradiCommand> aktivace,
            IHandle<DokoncitDefiniciNaradiInternalCommand> dokonceni)
        {
            this._definice = definice;
            this._aktivace = aktivace;
            this._dokonceni = dokonceni;
        }

        public void Handle(ZahajenaDefiniceNaradiEvent evt)
        {
            _definice.Handle(new DefinovatNaradiCommand
            {
                NaradiId = evt.NaradiId,
                Vykres = evt.Vykres,
                Rozmer = evt.Rozmer,
                Druh = evt.Druh
            });
        }

        public void Handle(ZahajenaAktivaceNaradiEvent evt)
        {
            _aktivace.Handle(new AktivovatNaradiCommand
            {
                NaradiId = evt.NaradiId
            });
        }

        public void Handle(DefinovanoNaradiEvent evt)
        {
            _dokonceni.Handle(new DokoncitDefiniciNaradiInternalCommand
            {
                NaradiId = evt.NaradiId,
                Vykres = evt.Vykres,
                Rozmer = evt.Rozmer,
                Druh = evt.Druh
            });
        }
    }
}
