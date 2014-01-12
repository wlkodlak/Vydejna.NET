using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface INetworkBus
    {
        void Send(MessageDestination destination, Message message, Action onComplete, Action<Exception> onError);
        void Receive(MessageDestination destination, bool nowait, Action<Message> onReceived, Action nothingNew, Action<Exception> onError);
        void Subscribe(string type, MessageDestination destination, bool unsubscribe, Action onComplete, Action<Exception> onError);
        void MarkProcessed(Message message, MessageDestination newDestination, Action onComplete, Action<Exception> onError);
        void DeleteAll(MessageDestination destination, Action onComplete, Action<Exception> onError);
    }
    public class Message
    {
        public string Source;
        public MessageDestination Destination, OriginalDestination;
        public string Type, Format, Body;
        public string MessageId, CorellationId;
        public DateTime CreatedOn;
    }
    public class MessageDestination
    {
        public readonly string NodeId;
        public readonly string ProcessName;
        private MessageDestination(string processName, string nodeId)
        {
            ProcessName = processName;
            NodeId = nodeId;
        }
        public static readonly MessageDestination Subscribers = new MessageDestination("__SPECIAL__", "subscribers");
        public static readonly MessageDestination Processed = new MessageDestination("__SPECIAL__", "processed");
        public static readonly MessageDestination DeadLetters = new MessageDestination("__SPECIAL__", "deadletters");
        public static MessageDestination For(string processName, string nodeId)
        {
            return new MessageDestination(processName, nodeId);
        }
        public override string ToString()
        {
            return string.Concat(ProcessName, "@", NodeId);
        }
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 9042830;
                if (ProcessName != null)
                    hash = hash ^ ProcessName.GetHashCode();
                if (NodeId != null)
                    hash = hash ^ NodeId.GetHashCode();
                return hash;
            }
        }
        public override bool Equals(object obj)
        {
            return Equals(obj as MessageDestination);
        }
        private bool Equals(MessageDestination oth)
        {
            return !ReferenceEquals(oth, null)
                && string.Equals(ProcessName, oth.ProcessName, StringComparison.Ordinal)
                && string.Equals(NodeId, oth.NodeId, StringComparison.Ordinal);
        }
        public static bool operator == (MessageDestination x, MessageDestination y)
        {
            if (ReferenceEquals(x, null))
                return ReferenceEquals(y, null);
            else
                return x.Equals(y);
        }
        public static bool operator !=(MessageDestination x, MessageDestination y)
        {
            return !(x == y);
        }
    }

    public class NetworkBusReceiveFinished : IQueuedExecutionDispatcher
    {
        private Action<Message> _onReceived;
        private Message _message;

        public NetworkBusReceiveFinished(Action<Message> onReceived, Message message)
        {
            _onReceived = onReceived;
            _message = message;
        }

        public void Execute()
        {
            _onReceived(_message);
        }
    }
}
