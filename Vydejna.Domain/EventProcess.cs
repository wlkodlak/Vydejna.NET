using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public class EventProcess
    {
        private IEventStreaming _streamer;
        private string _consumerName;

        public EventProcess(IEventStreaming streamer, string consumerName)
        {
            this._streamer = streamer;
            this._consumerName = consumerName;
        }

        public void Register<T>(IHandle<T> handler)
        {
            throw new NotImplementedException();
        }

        public void Start()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }
    }
}
