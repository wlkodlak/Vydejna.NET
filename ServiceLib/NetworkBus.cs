using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface INetworkBus : IDisposable
    {
        Task Send(MessageDestination destination, Message message);
        Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel);
        Task Subscribe(string type, MessageDestination destination, bool unsubscribe);
        Task MarkProcessed(Message message, MessageDestination newDestination);
        Task DeleteAll(MessageDestination destination);
    }
    public class Message
    {
        public string Source;
        public MessageDestination Destination;
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
        public static readonly MessageDestination Subscribers = new MessageDestination("subscribers", "__SPECIAL__");
        public static readonly MessageDestination Processed = new MessageDestination("processed", "__SPECIAL__");
        public static readonly MessageDestination DeadLetters = new MessageDestination("deadletters", "__SPECIAL__");
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

    public class NetworkBusNull : INetworkBus
    {
        private List<TaskCompletionSource<Message>> _orphanTasks;

        public NetworkBusNull()
        {
            _orphanTasks = new List<TaskCompletionSource<Message>>();
        }

        public Task Send(MessageDestination destination, Message message)
        {
            throw new NotSupportedException();
        }

        public Task<Message> Receive(MessageDestination destination, bool nowait, CancellationToken cancel)
        {
            if (nowait)
                return TaskUtils.FromResult<Message>(null);
            else if (cancel.IsCancellationRequested)
                return TaskUtils.CancelledTask<Message>();
            else 
            {
                var tcs = new TaskCompletionSource<Message>();
                if (cancel.CanBeCanceled)
                    cancel.Register(() => tcs.TrySetCanceled());
                else
                    _orphanTasks.Add(tcs);
                return tcs.Task;
            }
        }

        public Task Subscribe(string type, MessageDestination destination, bool unsubscribe)
        {
            return TaskUtils.CompletedTask();
        }

        public Task MarkProcessed(Message message, MessageDestination newDestination)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAll(MessageDestination destination)
        {
            return TaskUtils.CompletedTask();
        }

        public void Dispose()
        {
            foreach (var task in _orphanTasks)
                task.TrySetCanceled();
        }
    }
}
