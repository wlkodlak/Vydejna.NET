using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Gui.SeznamNaradi;

namespace Vydejna.Gui.Shell
{
    public interface IShell
    {
        void RunModule(DefinovatNaradiViewModel viewModel);
    }
}
