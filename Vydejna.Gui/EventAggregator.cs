using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vydejna.Gui
{
    public interface IEventPublisher
    {
        void Publish<T>(T msg);
    }
}
