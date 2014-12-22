using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLib
{
    public interface IHttpRouteConfigCommitable
    {
        void Commit();
    }

    public interface ISerializerPicker
    {
        IHttpSerializer PickDeserializer(
            IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);

        IHttpSerializer PickSerializer(
            IHttpServerStagedContext context, IEnumerable<IHttpSerializer> options, ISerializerPicker next);
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
        public static void To(
            this IHttpRouteCommonConfiguratorRoute self,
            Func<IHttpServerRawContext, IList<RequestParameter>, Task> handler)
        {
            self.To(new DelegatedHttpRouteHandler(handler));
        }

        public static void To(
            this IHttpRouteCommonConfiguratorRoute self,
            Func<IHttpServerStagedContext, Task> processor)
        {
            self.To(new DelegatedHttpProcessor(processor));
        }

        private class DelegatedHttpRouteHandler : IHttpRouteHandler
        {
            private readonly Func<IHttpServerRawContext, IList<RequestParameter>, Task> _handler;

            public DelegatedHttpRouteHandler(Func<IHttpServerRawContext, IList<RequestParameter>, Task> handler)
            {
                _handler = handler;
            }

            public Task Handle(IHttpServerRawContext raw, IList<RequestParameter> routeParameters)
            {
                return _handler(raw, routeParameters);
            }
        }

        private class DelegatedHttpProcessor : IHttpProcessor
        {
            private readonly Func<IHttpServerStagedContext, Task> _handler;

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