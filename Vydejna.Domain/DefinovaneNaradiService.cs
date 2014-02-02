using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class DefinovaneNaradiService
        : IWriteSeznamNaradi
        , IHandle<CommandExecution<DefinovatNaradiInternalCommand>>
        , IHandle<CommandExecution<DokoncitDefiniciNaradiInternalCommand>>
    {
        private log4net.ILog _log;
        private IDefinovaneNaradiRepository _repoNaradi;

        public DefinovaneNaradiService(IDefinovaneNaradiRepository repoNaradi, IUnikatnostNaradiRepository repoUnikatnost)
        {
            _log = log4net.LogManager.GetLogger(typeof(DefinovaneNaradiService));
            _repoNaradi = repoNaradi;
        }

        public void Handle(CommandExecution<AktivovatNaradiCommand> message)
        {
            new NaradiHandler<AktivovatNaradiCommand>(
                this, message,
                msg => msg.NaradiId,
                (log, msg) => log.DebugFormat("AktivovatNaradi: {0}", msg.NaradiId),
                (msg, naradi) => naradi.Aktivovat(),
                msg => null)
                .Execute();
        }
        public void Handle(CommandExecution<DeaktivovatNaradiCommand> message)
        {
            new NaradiHandler<DeaktivovatNaradiCommand>(
                this, message,
                msg => msg.NaradiId,
                (log, msg) => log.DebugFormat("DeaktivovatNaradi: {0}", msg.NaradiId),
                (msg, naradi) => naradi.Deaktivovat(),
                msg => null)
                .Execute();
        }

        public void Handle(CommandExecution<DefinovatNaradiInternalCommand> message)
        {
            new NaradiHandler<DefinovatNaradiInternalCommand>(
                this, message,
                msg => msg.NaradiId,
                (log, msg) => log.DebugFormat("DefinovatNaradiInternal: {0}, vykres {1}, rozmer {2}, druh {3}", msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh),
                (msg, naradi) => { },
                msg => DefinovaneNaradi.Definovat(msg.NaradiId, msg.Vykres, msg.Rozmer, msg.Druh))
                .Execute();
        }

        private class NaradiHandler<TCommand>
        {
            private DefinovaneNaradiService _parent;
            private CommandExecution<TCommand> _message;
            private Func<TCommand, Guid> _getId;
            private Action<log4net.ILog, TCommand> _logAction;
            private Action<TCommand, DefinovaneNaradi> _forExisting;
            private Func<TCommand, DefinovaneNaradi> _forNew;
            private Guid _id;

            public NaradiHandler(DefinovaneNaradiService parent, CommandExecution<TCommand> message,
                Func<TCommand, Guid> getId,
                Action<log4net.ILog, TCommand> logAction,
                Action<TCommand, DefinovaneNaradi> forExisting,
                Func<TCommand, DefinovaneNaradi> forMissing)
            {
                _parent = parent;
                _message = message;
                _getId = getId;
                _logAction = logAction;
                _forExisting = forExisting;
                _forNew = forMissing;
            }
            public void Execute()
            {
                try
                {
                    _logAction(_parent._log, _message.Command);
                    _id = _getId(_message.Command);
                    _parent._repoNaradi.Load(_id, NaradiNacteno, NaradiChybi, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
            private void NaradiNacteno(DefinovaneNaradi naradi)
            {
                try
                {
                    _forExisting(_message.Command, naradi);
                    _parent._repoNaradi.Save(naradi, _message.OnCompleted, Konflikt, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
            private void NaradiChybi()
            {
                try
                {
                    var naradi = _forNew(_message.Command);
                    if (naradi != null)
                        _parent._repoNaradi.Save(naradi, _message.OnCompleted, Konflikt, _message.OnError);
                    else
                        _message.OnCompleted();
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
                    _parent._repoNaradi.Load(_id, NaradiNacteno, NaradiChybi, _message.OnError);
                }
                catch (Exception ex)
                {
                    _message.OnError(ex);
                }
            }
        }
    }
}
