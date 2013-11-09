using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Tests
{
    public class PropertyChangedWatcher
    {
        private bool _wholeChange;
        private readonly List<string> _changes;

        public PropertyChangedWatcher(INotifyPropertyChanged obj)
        {
            _changes = new List<string>();
            obj.PropertyChanged += PropertyChangedHandler;
        }

        public IList<string> GetChanges()
        {
            return _changes;
        }

        private void PropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
                _wholeChange = true;
            else
                _changes.Add(e.PropertyName ?? "");
        }

        public void AssertChange(string property)
        {
            if (!_wholeChange)
                CollectionAssert.Contains(_changes, property, "Property {0} was not changed", property);
        }
    }
}
