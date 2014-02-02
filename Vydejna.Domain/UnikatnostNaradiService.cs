using ServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class UnikatnostNaradiService
        : IHandle<CommandExecution<DefinovatNaradiCommand>>
        , IHandle<CommandExecution<DokoncitDefiniciNaradiInternalCommand>>
    {
        private log4net.ILog _log;
        private IUnikatnostNaradiRepository _repoUnikatnost;

        public UnikatnostNaradiService(IUnikatnostNaradiRepository repoUnikatnost)
        {
            _log = log4net.LogManager.GetLogger(typeof(UnikatnostNaradiService));
            _repoUnikatnost = repoUnikatnost;
        }

        public void Handle(CommandExecution<DefinovatNaradiCommand> message)
        {
            new UnikatnostHandler<DefinovatNaradiCommand>(
                this, message,
                (log, msg) => log.DebugFormat("DefinovatNaradi: {0}, vykres {1}, rozmer {2}, druh {3}", msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh),
                (msg, unikatnost) => unikatnost.ZahajitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }

        public void Handle(CommandExecution<DokoncitDefiniciNaradiInternalCommand> message)
        {
            new UnikatnostHandler<DokoncitDefiniciNaradiInternalCommand>(
                this, message,
                (log, msg) => log.DebugFormat("DokoncitDefiniciNaradiInternal: {0}, vykres {1}, rozmer {2}, druh {3}", msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh),
                (msg, unikatnost) => unikatnost.DokoncitDefinici(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }

        private class UnikatnostHandler<TCommand>
        {
            private UnikatnostNaradiService _parent;
            private CommandExecution<TCommand> _message;
            private Action<log4net.ILog, TCommand> _logAction;
            private Action<TCommand, UnikatnostNaradi> _action;

            public UnikatnostHandler(UnikatnostNaradiService parent, CommandExecution<TCommand> message,
                Action<log4net.ILog, TCommand> logAction,
                Action<TCommand, UnikatnostNaradi> action)
            {
                _parent = parent;
                _message = message;
                _logAction = logAction;
                _action = action;
            }
            public void Execute()
            {
                try
                {
                    _logAction(_parent._log, _message.Command);
                    _parent._repoUnikatnost.Load(UnikatnostNactena, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
            private void UnikatnostNactena(UnikatnostNaradi unikatnost)
            {
                try
                {
                    _action(_message.Command, unikatnost);
                    _parent._repoUnikatnost.Save(unikatnost, _message.OnCompleted, Konflikt, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
            private void Konflikt()
            {
                try
                {
                    _parent._repoUnikatnost.Load(UnikatnostNactena, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
        }
    }
}
