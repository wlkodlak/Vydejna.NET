using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Domain
{
    public interface INaradiRepository
    {
        Naradi Get(Guid id);
        void Save(Naradi naradi);
    }
}
