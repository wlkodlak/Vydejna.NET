using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ServiceLib
{
    public class ProcessManagerPublisher : IPublisher, IDisposable
    {
        private readonly DatabasePostgres _db;
        private readonly CancellationTokenSource _cancel;
        private readonly IBus _bus;
        private readonly Dictionary<Type, Action<object>> _publishers;
        private readonly string _notificationName;

        public ProcessManagerPublisher(DatabasePostgres db, IBus relayBus, string notificationName = "processmanager")
        {
            _db = db;
            _bus = relayBus;
            _cancel = new CancellationTokenSource();
            _publishers = new Dictionary<Type, Action<object>>();
            _notificationName = notificationName;

            AddSerializer<ProcessManagerMessages.ElectionsInquiry>(msg => SerializeMessage("ElectionsInquiry", msg.SenderId, msg.ElectionsId, msg.ForfitCandidature ? "true" : "false"));
            AddSerializer<ProcessManagerMessages.ElectionsCandidate>(msg => SerializeMessage("ElectionsCandidate", msg.SenderId, msg.ElectionsId));
            AddSerializer<ProcessManagerMessages.ElectionsLeader>(msg => SerializeMessage("ElectionsLeader", msg.SenderId, msg.ElectionsId));
            AddSerializer<ProcessManagerMessages.Heartbeat>(msg => SerializeMessage("Heartbeat", msg.SenderId));
            AddSerializer<ProcessManagerMessages.HeartStopped>(msg => SerializeMessage("HeartStopped", msg.SenderId));
            AddSerializer<ProcessManagerMessages.ProcessStart>(msg => SerializeMessage("ProcessStart", msg.SenderId, msg.AssignedNode, msg.ProcessName));
            AddSerializer<ProcessManagerMessages.ProcessStop>(msg => SerializeMessage("ProcessStop", msg.SenderId, msg.AssignedNode, msg.ProcessName));
            AddSerializer<ProcessManagerMessages.ProcessChange>(msg => SerializeMessage("ProcessChange", msg.SenderId, msg.ProcessName, msg.NewState.ToString()));

            _db.Listen(DatabasePostgres.ConnectionNotification, OnConnected, _cancel.Token);
            _db.Listen(_notificationName, OnReceived, _cancel.Token);
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _cancel.Dispose();
        }

        private void OnConnected(string payload)
        {
            if (payload == "CONNECTED")
                _bus.Publish(new ProcessManagerMessages.ConnectionRestored());
        }

        private void OnReceived(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return;
            var parts = payload.Split(':');
            switch (parts[0])
            {
                case "ElectionsInquiry":
                    _bus.Publish(new ProcessManagerMessages.ElectionsInquiry
                    {
                        SenderId = parts[1],
                        ElectionsId = parts[2],
                        ForfitCandidature = parts[3] == "true"
                    });
                    break;
                case "ElectionsCandidate":
                    _bus.Publish(new ProcessManagerMessages.ElectionsCandidate
                    {
                        SenderId = parts[1],
                        ElectionsId = parts[2]
                    });
                    break;
                case "ElectionsLeader":
                    _bus.Publish(new ProcessManagerMessages.ElectionsLeader
                    {
                        SenderId = parts[1],
                        ElectionsId = parts[2]
                    });
                    break;
                case "Heartbeat":
                    _bus.Publish(new ProcessManagerMessages.Heartbeat
                    {
                        SenderId = parts[1]
                    });
                    break;
                case "HeartStopped":
                    _bus.Publish(new ProcessManagerMessages.HeartStopped
                    {
                        SenderId = parts[1]
                    });
                    break;
                case "ProcessStart":
                    _bus.Publish(new ProcessManagerMessages.ProcessStart
                    {
                        SenderId = parts[1],
                        AssignedNode = parts[2],
                        ProcessName = parts[3]
                    });
                    break;
                case "ProcessStop":
                    _bus.Publish(new ProcessManagerMessages.ProcessStop
                    {
                        SenderId = parts[1],
                        AssignedNode = parts[2],
                        ProcessName = parts[3]
                    });
                    break;
                case "ProcessChange":
                    ProcessState newProcessState;
                    if (parts.Length == 4 && Enum.TryParse(parts[3], out newProcessState))
                    {
                        _bus.Publish(new ProcessManagerMessages.ProcessChange
                        {
                            SenderId = parts[1],
                            ProcessName = parts[2],
                            NewState = newProcessState
                        });
                    }
                    break;
            }
        }

        public void Publish<T>(T message)
        {
            _publishers[typeof(T)](message);
        }

        private void AddSerializer<T>(Func<T, string> serializer)
        {
            _publishers[typeof(T)] = raw => _db.Notify(_notificationName, serializer((T)raw));
        }

        private string SerializeMessage(string type, string sender, params string[] parameters)
        {
            var sb = new StringBuilder().Append(type).Append(":").Append(sender);
            foreach (var param in parameters)
                sb.Append(":").Append(param);
            return sb.ToString();
        }
    }
}
