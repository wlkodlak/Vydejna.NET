using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Tests
{
    public class PropertyErrorWatcher
    {
        private IDataErrorInfo _errorSource;
        private INotifyPropertyChanged _changesSource;
        private List<string> _changes = new List<string>();
        private HashSet<string> _watched = new HashSet<string>();
        private Dictionary<string, string> _errorsOnChanges = new Dictionary<string, string>();
        public const string AnyError = "***ANY ERROR***";
        public const string NoError = null;

        public PropertyErrorWatcher(IDataErrorInfo source, params string[] watched)
        {
            _errorSource = source;
            _changesSource = source as INotifyPropertyChanged;
            if (_changesSource != null)
            {
                _changesSource.PropertyChanged += PropertyChangedHandler;
                foreach (var item in watched)
                {
                    _watched.Add(item);
                    RecordError(item);
                }
            }
        }

        private void RecordError(string item)
        {
            _errorsOnChanges[item] = _errorSource[item];
        }

        private string GetError(string item)
        {
            if (_watched.Contains(item))
                return _errorsOnChanges[item];
            else
                return _errorSource[item];
        }

        private void PropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                foreach (var item in _watched)
                {
                    _changes.Add(item);
                    RecordError(item);
                }
            }
            else
            {
                _changes.Add(e.PropertyName);
                RecordError(e.PropertyName);
            }
        }

        public void AssertError(string property, string expected)
        {
            var actual = GetError(property);
            if (string.IsNullOrEmpty(expected))
            {
                if (!string.IsNullOrEmpty(actual))
                    Assert.Fail("Property {0} should not have error, but had: {1}", property, actual);
            }
            else
            {
                if (string.IsNullOrEmpty(actual))
                    Assert.Fail("Property {0} should have an error, but had none.", property);
                else if (expected != AnyError && expected != actual)
                    Assert.Fail("Property {0} should have error {1}, but had {2}.", property, expected, actual);
            }
        }
    }
}
