using ServiceLib;
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

        public void Execute(NecislovaneNaradiPrijmoutNaVydejnuCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiVydatDoVyrobyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPrijmoutZVyrobyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPredatKOpraveCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPrijmoutZOpravyCommand cmd, ITime time)
        {
        }

        public void Execute(NecislovaneNaradiPredatKeSesrotovaniCommand cmd, ITime time)
        {
        }
    }
}
