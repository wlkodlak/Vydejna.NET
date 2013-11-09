using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Gui
{
    public abstract class ViewModelBase : INotifyPropertyChanged, IDataErrorInfo
    {
        private struct ErrorInfo
        {
            public string Property, Message;
        }

        private List<ErrorInfo> _errors = new List<ErrorInfo>();

        protected void SetProperty<T>(string name, ref T field, T value)
        {
            if (Equals(field, value))
                return;
            field = value;
            RaisePropertyChanged(name);
        }

        protected void RaisePropertyChanged(string property)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(property));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        string IDataErrorInfo.Error
        {
            get { return string.Join(Environment.NewLine, _errors.Select(e => e.Message)); }
        }

        string IDataErrorInfo.this[string property]
        {
            get { return _errors.Where(e => property == e.Property).Select(e => e.Message).FirstOrDefault() ?? ""; }
        }

        protected void ClearErrors(string property)
        {
            if (string.IsNullOrEmpty(property))
                _errors.Clear();
            else
                _errors.RemoveAll(e => e.Property == property);
        }

        protected void AddError(string property, string message)
        {
            _errors.Add(new ErrorInfo { Property = property, Message = message });
        }

        protected bool HasErrors { get { return _errors.Count != 0; } }
    }
}
