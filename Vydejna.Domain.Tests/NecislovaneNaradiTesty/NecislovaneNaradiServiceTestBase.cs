using Vydejna.Domain.Tests.NaradiObecneTesty;

namespace Vydejna.Domain.Tests.NecislovaneNaradiTesty
{
    public class NecislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<NecislovaneNaradi, NecislovaneNaradiService>
    {
        protected override NecislovaneNaradiService CreateService()
        {
            return new NecislovaneNaradiService(_repository, _time);
        }
    }
}
