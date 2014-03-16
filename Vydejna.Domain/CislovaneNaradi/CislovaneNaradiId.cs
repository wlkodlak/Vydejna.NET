using ServiceLib;
using System;

namespace Vydejna.Domain.CislovaneNaradi
{
    public class CislovaneNaradiId : IAggregateId
    {
        public readonly Guid NaradiId;
        public readonly int CisloNaradi;

        public CislovaneNaradiId(Guid naradiId, int cisloNaradi)
        {
            NaradiId = naradiId;
            CisloNaradi = cisloNaradi;
        }

        public override int GetHashCode()
        {
            return NaradiId.GetHashCode() ^ CisloNaradi;
        }

        public override bool Equals(object obj)
        {
            var oth = obj as CislovaneNaradiId;
            return oth != null && NaradiId == oth.NaradiId && CisloNaradi == oth.CisloNaradi;
        }

        public override string ToString()
        {
            return string.Concat(NaradiId.ToString("N").ToLowerInvariant(), "-", CisloNaradi.ToString());
        }
    }
}
