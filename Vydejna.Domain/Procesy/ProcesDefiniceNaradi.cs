using log4net;
using ServiceLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain.Procesy
{
    public class ProcesDefiniceNaradi
        : ISubscribeToEventManager
        , IProcessEvent<ZahajenaDefiniceNaradiEvent>
        , IProcessEvent<ZahajenaAktivaceNaradiEvent>
        , IProcessEvent<DefinovanoNaradiEvent>
    {
        private IPublisher _bus;
        private static readonly ILog Logger = LogManager.GetLogger("Vydejna.Domain.ProcesDefiniceNaradi");

        public ProcesDefiniceNaradi(IPublisher bus)
        {
            _bus = bus;
        }

        public void Subscribe(IEventSubscriptionManager subscriptions)
        {
            subscriptions.Register<ZahajenaDefiniceNaradiEvent>(this);
            subscriptions.Register<ZahajenaAktivaceNaradiEvent>(this);
            subscriptions.Register<DefinovanoNaradiEvent>(this);
        }

        public Task Handle(ZahajenaDefiniceNaradiEvent evt)
        {
            return TaskUtils.FromEnumerable(HandleInternal(evt)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(ZahajenaDefiniceNaradiEvent evt)
        {
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                var command = new DefinovatNaradiInternalCommand
                {
                    NaradiId = evt.NaradiId,
                    Vykres = evt.Vykres,
                    Rozmer = evt.Rozmer,
                    Druh = evt.Druh
                };
                var taskSend = _bus.SendCommand(command);
                yield return taskSend;
                if (taskSend.Exception != null || taskSend.IsCanceled)
                    yield break;
                if (taskSend.Result.Status == CommandResultStatus.Success)
                {
                    Logger.InfoFormat(
                        "Zahajena definice naradi {0}, vykres {1}, rozmer {2}, odesilam DefinovatNaradiInternal. ({3} ms)",
                        evt.NaradiId, evt.Vykres, evt.Rozmer, evt.Druh, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Logger.ErrorFormat("Pokus o definici naradi {0} (vykres {1}, rozmer {2}) selhal: {3}",
                        evt.NaradiId, evt.Vykres, evt.Rozmer, taskSend.Result.Errors.First().Message);
                }
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public Task Handle(ZahajenaAktivaceNaradiEvent evt)
        {
            return TaskUtils.FromEnumerable(HandleInternal(evt)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(ZahajenaAktivaceNaradiEvent evt)
        {
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                var command = new AktivovatNaradiCommand
                {
                    NaradiId = evt.NaradiId
                };
                var taskSend = _bus.SendCommand(command);
                yield return taskSend;

                if (taskSend.Exception != null || taskSend.IsCanceled)
                    yield break;
                if (taskSend.Result.Status == CommandResultStatus.Success)
                {
                    Logger.InfoFormat(
                        "Zahajena aktivace naradi {0}, posilam AktivovatNaradi. ({1} ms)",
                        evt.NaradiId, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Logger.ErrorFormat("Pokus o aktivaci naradi {0} selhal: {1}",
                        evt.NaradiId, taskSend.Result.Errors.First().Message);
                }
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public Task Handle(DefinovanoNaradiEvent evt)
        {
            return TaskUtils.FromEnumerable(HandleInternal(evt)).GetTask();
        }

        private IEnumerable<Task> HandleInternal(DefinovanoNaradiEvent evt)
        {
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                var command = new DokoncitDefiniciNaradiInternalCommand
                {
                    NaradiId = evt.NaradiId,
                    Vykres = evt.Vykres,
                    Rozmer = evt.Rozmer,
                    Druh = evt.Druh
                };
                var taskSend = _bus.SendCommand(command);
                yield return taskSend;

                if (taskSend.Exception != null || taskSend.IsCanceled)
                    yield break;
                var commandResult = taskSend.Result;

                if (commandResult.Status == CommandResultStatus.Success)
                {
                    Logger.InfoFormat(
                        "Definovano naradi {0}, vykres {1}, rozmer {2}, posilam DokoncitDefiniciNaradiInternal. ({3} ms)",
                        evt.NaradiId, evt.Vykres, evt.Rozmer, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Logger.ErrorFormat("Pokus o dokonceni definice naradi {0} (vykres {1}, rozmer {2}) selhal: {3}",
                        evt.NaradiId, evt.Vykres, evt.Rozmer, commandResult.Errors.First().Message);
                }
            }
            finally
            {
                stopwatch.Stop();
            }
        }
    }
}
