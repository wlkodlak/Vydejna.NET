using ServiceLib;
using System;
using System.Reflection;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class NecislovaneNaradi : EventSourcedGuidAggregate
    {
        public NecislovaneNaradi()
        {
            RegisterEventHandlers(GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public void Execute(NecislovaneNaradiPrijmoutNaVydejnuCommand necislovaneNaradiPrijmoutNaVydejnuCommand, ITime _time)
        {
            throw new NotImplementedException();
        }

        public void Execute(NecislovaneNaradiVydatDoVyrobyCommand necislovaneNaradiVydatDoVyrobyCommand, ITime _time)
        {
            throw new NotImplementedException();
        }

        public void Execute(NecislovaneNaradiPrijmoutZVyrobyCommand necislovaneNaradiPrijmoutZVyrobyCommand, ITime _time)
        {
            throw new NotImplementedException();
        }

        public void Execute(NecislovaneNaradiPredatKOpraveCommand necislovaneNaradiPredatKOpraveCommand, ITime _time)
        {
            throw new NotImplementedException();
        }

        public void Execute(NecislovaneNaradiPrijmoutZOpravyCommand necislovaneNaradiPrijmoutZOpravyCommand, ITime _time)
        {
            throw new NotImplementedException();
        }

        public void Execute(NecislovaneNaradiPredatKeSesrotovaniCommand necislovaneNaradiPredatKeSesrotovaniCommand, ITime _time)
        {
            throw new NotImplementedException();
        }
    }
}
