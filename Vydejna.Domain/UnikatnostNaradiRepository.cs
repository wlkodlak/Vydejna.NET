using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface IUnikatnostNaradiRepository
    {
        UnikatnostNaradi Get();
        void Save(UnikatnostNaradi unikatnost);
    }
}
