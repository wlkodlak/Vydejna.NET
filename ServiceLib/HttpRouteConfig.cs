using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

namespace ServiceLib
{
    public interface IHttpRouteConfigCommitable
    {
        void Commit();
    }
    public interface ISerializerPicker
    {
        IHttpSerializer PickDeserializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);
        IHttpSerializer PickSerializer(IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);
    }
    public interface IHttpProcessor
    {
        Task Process(IHttpServerStagedContext context);
    }

    public interface IHttpRouteCommonConfigurator : IHttpRouteConfigCommitable
    {
        IHttpRouteCommonConfigurator SubRouter();
        IHttpRouteCommonConfigurator ForFolder(string path);
        IHttpRouteCommonConfigurator WithPicker(ISerializerPicker picker);
        IHttpRouteCommonConfigurator WithSerializer(IHttpSerializer serializer);
        IHttpRouteCommonConfiguratorRoute Route(string path);
    }
    public interface IHttpRouteCommonConfiguratorRoute
    {
        IHttpRouteCommonConfiguratorRoute WithPicker(ISerializerPicker picker);
        IHttpRouteCommonConfiguratorRoute WithSerializer(IHttpSerializer serializer);
        IHttpRouteConfigCommitable To(IHttpRouteHandler handler);
        IHttpRouteConfigCommitable To(IHttpProcessor processor);
    }
    public interface IHttpRouteCommonConfiguratorExtended : IHttpRouteCommonConfigurator
    {
        void Register(IHttpRouteConfigCommitable subRouter);
        IHttpAddRoute GetRouter();
        string GetPath();
        ISerializerPicker GetPicker();
        IList<IHttpSerializer> GetSerializers();
    }
    public static class HttpRouteCommonConfiguratorRouteExtensions
    {
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Func<IHttpServerRawContext, Task> handler)
        {
            self.To(new DelegatedHttpRouteHandler(handler));
        }
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Func<IHttpServerStagedContext, Task> processor)
        {
            self.To(new DelegatedHttpProcessor(processor));
        }
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Func<IHttpServerStagedContext, IEnumerable<Task>> processor)
        {
            self.To(new DelegatedEnumerableHttpProcessor(processor));
        }
        private class DelegatedHttpRouteHandler : IHttpRouteHandler
        {
            Func<IHttpServerRawContext, Task> _handler;
            public DelegatedHttpRouteHandler(Func<IHttpServerRawContext, Task> handler)
            {
                _handler = handler;
            }
            public Task Handle(IHttpServerRawContext context)
            {
                return _handler(context);
            }
        }
        private class DelegatedHttpProcessor : IHttpProcessor
        {
            Func<IHttpServerStagedContext, Task> _handler;
            public DelegatedHttpProcessor(Func<IHttpServerStagedContext, Task> handler)
            {
                _handler = handler;
            }
            public Task Process(IHttpServerStagedContext context)
            {
                return _handler(context);
            }
        }
        private class DelegatedEnumerableHttpProcessor : IHttpProcessor
        {
            Func<IHttpServerStagedContext, IEnumerable<Task>> _handler;
            public DelegatedEnumerableHttpProcessor(Func<IHttpServerStagedContext, IEnumerable<Task>> handler)
            {
                _handler = handler;
            }
            public Task Process(IHttpServerStagedContext context)
            {
                return TaskUtils.FromEnumerable(_handler(context)).GetTask();
            }
        }
    }
}
