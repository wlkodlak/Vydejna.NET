using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain.Procesy
{
    public class ProcesDefiniceNaradi
        : IHandle<CommandExecution<ZahajenaDefiniceNaradiEvent>>
        , IHandle<CommandExecution<ZahajenaAktivaceNaradiEvent>>
        , IHandle<CommandExecution<DefinovanoNaradiEvent>>
    {
        private IHandle<CommandExecution<DefinovatNaradiCommand>> _definice;
        private IHandle<CommandExecution<AktivovatNaradiCommand>> _aktivace;
        private IHandle<CommandExecution<DokoncitDefiniciNaradiInternalCommand>> _dokonceni;

        public ProcesDefiniceNaradi(
            IHandle<CommandExecution<DefinovatNaradiCommand>> definice,
            IHandle<CommandExecution<AktivovatNaradiCommand>> aktivace,
            IHandle<CommandExecution<DokoncitDefiniciNaradiInternalCommand>> dokonceni)
        {
            this._definice = definice;
            this._aktivace = aktivace;
            this._dokonceni = dokonceni;
        }

        private void Handle<T>(IHandle<CommandExecution<T>> handler, T cmd, Action onCompleted, Action<Exception> onError)
        {
            handler.Handle(new CommandExecution<T>(cmd, onCompleted, onError));
        }

        public void Handle(CommandExecution<ZahajenaDefiniceNaradiEvent> evt)
        {
            Handle(_definice, new DefinovatNaradiCommand
            {
                NaradiId = evt.Command.NaradiId,
                Vykres = evt.Command.Vykres,
                Rozmer = evt.Command.Rozmer,
                Druh = evt.Command.Druh
            }, evt.OnCompleted, evt.OnError);
        }

        public void Handle(CommandExecution<ZahajenaAktivaceNaradiEvent> evt)
        {
            Handle(_aktivace, new AktivovatNaradiCommand
            {
                NaradiId = evt.Command.NaradiId
            }, evt.OnCompleted, evt.OnError);
        }

        public void Handle(CommandExecution<DefinovanoNaradiEvent> evt)
        {
            Handle(_dokonceni, new DokoncitDefiniciNaradiInternalCommand
            {
                NaradiId = evt.Command.NaradiId,
                Vykres = evt.Command.Vykres,
                Rozmer = evt.Command.Rozmer,
                Druh = evt.Command.Druh
            }, evt.OnCompleted, evt.OnError);
        }
    }
}
