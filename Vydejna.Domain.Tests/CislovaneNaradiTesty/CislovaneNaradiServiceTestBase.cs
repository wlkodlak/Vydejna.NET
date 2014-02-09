using Vydejna.Domain.Tests.NaradiObecneTesty;

namespace Vydejna.Domain.Tests.CislovaneNaradiTesty
{
    public class CislovaneNaradiServiceTestBase : ObecneNaradiServiceTestBase<CislovaneNaradi, CislovaneNaradiService>
    {
        protected override CislovaneNaradiService CreateService()
        {
            return new CislovaneNaradiService(_repository, _time);
        }
    }
}
