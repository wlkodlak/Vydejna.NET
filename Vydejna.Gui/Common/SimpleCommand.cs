using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Vydejna.Gui.Common
{
    public class SimpleCommand : ICommand
    {
        private bool _enabled = true;
        private Action _actionVoid;
        private Action<object> _actionObject;

        public SimpleCommand(Action action)
        {
            _actionVoid = action;
        }

        public SimpleCommand(Action<object> action)
        {
            _actionObject = action;
        }

        public bool CanExecute(object parameter)
        {
            return _enabled;
        }

        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter)
        {
            if (_enabled)
            {
                if (_actionObject != null)
                    _actionObject(parameter);
                else if (_actionVoid != null)
                    _actionVoid();
            }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (value == _enabled)
                    return;
                _enabled = value;
                var handler = CanExecuteChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }
    }
}
