using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace ServiceLib
{
    public interface ILogContextMessage : IEnumerable<LogContextMessageProperty>
    {
        TraceEventType Level { get; }
        string SummaryFormat { get; }
        object GetProperty(string name);
    }

    public struct LogContextMessageProperty
    {
        public string Name;
        public bool IsLong;
        public object Value;
    }

    public class LogContextMessage : ILogContextMessage
    {
        private readonly TraceEventType _level;
        private readonly int _eventId;
        private readonly Dictionary<string, LogContextMessageProperty> _properties;

        public LogContextMessage(TraceEventType level, int eventId, string summary)
        {
            _level = level;
            _eventId = eventId;
            SummaryFormat = summary;
            _properties = new Dictionary<string, LogContextMessageProperty>();
        }

        public TraceEventType Level { get { return _level; } }

        public string SummaryFormat { get; set; }

        public void SetProperty(string name, bool isLong, object value)
        {
            _properties[name] = new LogContextMessageProperty
            {
                Name = name,
                IsLong = isLong,
                Value = value
            };
        }

        public void Log(TraceSource trace)
        {
            trace.TraceData(_level, _eventId, this);
        }

        public override string ToString()
        {
            bool first;
            var sb = new StringBuilder();

            var summaryGenerator = new LogContextSummaryGenerator(this);
            sb.Append(summaryGenerator.Generate());

            first = true;
            foreach (var property in _properties.Values)
            {
                if (property.IsLong)
                    continue;
                if (first)
                    first = false;
                else
                    sb.Append(",");
                sb.Append(" ");
                sb.Append(property.Name).Append("=");
                sb.Append(property.Value);
            }

            first = true;
            foreach (var property in _properties.Values)
            {
                if (!property.IsLong)
                    continue;
                if (first)
                {
                    first = false;
                    sb.AppendLine();
                }
                sb.Append(property.Name).AppendLine(":");
                if (property.Value != null)
                    sb.Append(property.Value.ToString());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public object GetProperty(string name)
        {
            LogContextMessageProperty property;
            if (!_properties.TryGetValue(name, out property))
                return null;
            return property.Value;
        }

        public IEnumerator<LogContextMessageProperty> GetEnumerator()
        {
            return _properties.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class LogContextSummaryElement
    {
        public string PropertyName { get; private set; }
        public string FixedText { get; private set; }
        public object PropertyValue { get; private set; }

        private LogContextSummaryElement() { }

        public static LogContextSummaryElement CreateFixed(string text)
        {
            return new LogContextSummaryElement { FixedText = text };
        }

        public static LogContextSummaryElement CreateVariable(string property)
        {
            return new LogContextSummaryElement { PropertyName = property };
        }

        public LogContextSummaryElement LoadValueFrom(ILogContextMessage message)
        {
            if (!string.IsNullOrEmpty(PropertyName))
                PropertyValue = message.GetProperty(PropertyName);
            return this;
        }

        public override string ToString()
        {
            if (FixedText != null)
                return FixedText;
            if (string.IsNullOrEmpty(PropertyName) || PropertyValue == null)
                return "";
            return PropertyValue.ToString();
        }
    }

    public class LogContextSummaryParser : IEnumerator<LogContextSummaryElement>
    {
        private char[] _format;
        private int _state;
        private int _blockStart;
        private int _formatLength;
        private int _currentPosition;
        private LogContextSummaryElement _current;
        private bool _emitted;

        public LogContextSummaryParser(string format)
        {
            _format = format.ToCharArray();
            Reset();
        }

        public bool MoveNext()
        {
            _emitted = false;
            while (!_emitted)
            {
                if (_state == -1)
                    StateEnd();
                else if (_state == 0)
                    State0();
                else if (_state == 1)
                    State1();
                else if (_state == 2)
                    State2();
                else if (_state == 3)
                    State3();
                else if (_state == 4)
                    State4();
                else
                    throw new InvalidOperationException("Unknown internal state");
            }
            return _current != null;
        }

        private void StateEnd()
        {
            EmitFixedChar(null);
        }

        private void State0()
        {
            if (IsEof())
            {
                EmitFixedChar(null);
                GotoState(-1);
            }
            else
            {
                if (IsChar('{'))
                {
                    GotoState(2);
                    ReadChar();
                }
                else if (IsChar('}'))
                {
                    GotoState(4);
                    ReadChar();
                }
                else
                {
                    GotoState(1);
                    StartBlock();
                    ReadChar();
                }
            }
        }

        private void State1()
        {
            if (IsEof())
            {
                EmitFixedBlock();
                GotoState(-1);
            }
            else
            {
                if (IsChar('{') || IsChar('}'))
                {
                    EmitFixedBlock();
                    GotoState(0);
                }
                else
                {
                    ReadChar();
                }
            }
        }

        private void State2()
        {
            if (IsEof())
                throw new FormatException("Invalid format: " + new string(_format));
            if (IsChar('{'))
            {
                EmitFixedChar("{");
                GotoState(0);
                ReadChar();
            }
            else if (IsChar('}'))
            {
                EmitFixedChar("}");
                GotoState(0);
                ReadChar();
            }
            else
            {
                StartBlock();
                GotoState(3);
                ReadChar();
            }
        }

        private void State3()
        {
            if (IsEof())
                throw new FormatException("Invalid format: " + new string(_format));
            if (IsChar('}'))
            {
                EmitVariableBlock();
                GotoState(0);
                ReadChar();
            }
            else
            {
                ReadChar();
            }
        }

        private void State4()
        {
            if (IsChar('}'))
            {
                EmitFixedChar("}");
                GotoState(0);
                ReadChar();
            }
            else
                throw new FormatException("Invalid format: " + new string(_format));
        }

        private bool IsEof()
        {
            return _currentPosition >= _formatLength;
        }

        private bool IsChar(char c)
        {
            return _currentPosition < _formatLength && _format[_currentPosition] == c;
        }

        private void GotoState(int state)
        {
            _state = state;
        }

        private void ReadChar()
        {
            if (_state != -1 && _currentPosition < _formatLength)
                _currentPosition++;
        }

        private void StartBlock()
        {
            _blockStart = _currentPosition;
        }

        private void EmitFixedBlock()
        {
            _emitted = true;
            _current = LogContextSummaryElement.CreateFixed(new string(_format, _blockStart, _currentPosition - _blockStart));
        }

        private void EmitFixedChar(string text)
        {
            _emitted = true;
            if (text == null)
                _current = null;
            else
                _current = LogContextSummaryElement.CreateFixed(text);
        }

        private void EmitVariableBlock()
        {
            _emitted = true;
            _current = LogContextSummaryElement.CreateVariable(new string(_format, _blockStart, _currentPosition - _blockStart));
        }

        public LogContextSummaryElement Current
        {
            get { return _current; }
        }

        public void Dispose()
        {
        }

        object System.Collections.IEnumerator.Current
        {
            get { return _current; }
        }

        public void Reset()
        {
            _state = 0;
            _blockStart = 0;
            _currentPosition = 0;
            _formatLength = _format.Length;
        }
    }

    public class LogContextSummaryGenerator
    {
        private ILogContextMessage _message;

        public LogContextSummaryGenerator(ILogContextMessage message)
        {
            _message = message;
        }

        public string Generate()
        {
            var builder = new StringBuilder();
            var parser = new LogContextSummaryParser(_message.SummaryFormat);
            while (parser.MoveNext())
            {
                var element = parser.Current;
                element.LoadValueFrom(_message);
                builder.Append(element);
            }
            return builder.ToString();
        }
    }
}
