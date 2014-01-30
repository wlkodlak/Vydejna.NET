using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IMemoryCache<T>
    {
        void Get(string key, Action<int, T> onLoaded, Action<Exception> onError, Action<IMemoryCacheLoad<T>> onLoading);
        void Evict(string key);
        void EvictOldEntries(int ticks);
    }

    public interface IMemoryCacheLoad<T>
    {
        string Key { get; }
        int OldVersion { get; }
        T OldValue { get; }
        int Validity { get; set; }
        int Expiration { get; set; }
        void SetLoadedValue(int version, T value);
        void ValueIsStillValid();
        void LoadingFailed(Exception exception);
        IMemoryCacheLoad<T> Expires(int validity, int expiration);
    }

    public class MemoryCache<T> : IMemoryCache<T>
    {
        public void Get(string key, Action<int, T> onLoaded, Action<Exception> onError, Action<IMemoryCacheLoad<T>> onLoading)
        {
            throw new NotImplementedException();
        }

        public void Evict(string key)
        {
            throw new NotImplementedException();
        }

        public void EvictOldEntries(int ticks)
        {
            throw new NotImplementedException();
        }
    }
}
