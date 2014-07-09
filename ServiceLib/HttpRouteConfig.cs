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
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Func<IHttpServerRawContext, IList<RequestParameter>, Task> handler)
        {
            self.To(new DelegatedHttpRouteHandler(handler));
        }
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Func<IHttpServerStagedContext, Task> processor)
        {
            self.To(new DelegatedHttpProcessor(processor));
        }
        private class DelegatedHttpRouteHandler : IHttpRouteHandler
        {
            Func<IHttpServerRawContext, IList<RequestParameter>, Task> _handler;
            public DelegatedHttpRouteHandler(Func<IHttpServerRawContext, IList<RequestParameter>, Task> handler)
            {
                _handler = handler;
            }
            public Task Handle(IHttpServerRawContext context, IList<RequestParameter> routeParameters)
            {
                return _handler(context, routeParameters);
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
    }
}
