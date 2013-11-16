using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;

namespace Vydejna.Domain
{
    public interface IDocumentStore
    {
        Task<string> GetDocument(string key);
        Task SaveDocument(string key, string value);
    }

    public class DocumentStoreInMemory : IDocumentStore
    {
        private ConcurrentDictionary<string, string> _data;

        public DocumentStoreInMemory()
        {
            _data = new ConcurrentDictionary<string, string>();
        }

        public Task<string> GetDocument(string key)
        {
            string value;
            if (_data.TryGetValue(key, out value))
                return TaskResult.GetCompletedTask(value);
            else
                return TaskResult.GetCompletedTask("");
        }

        public Task SaveDocument(string key, string value)
        {
            _data[key] = value;
            return TaskResult.GetCompletedTask();
        }
    }
}
