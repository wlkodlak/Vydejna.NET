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
        void StartProcessing(IHttpServerStagedContext context);
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
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Action<IHttpServerRawContext> handler)
        {
            self.To(new DelegatedHttpRouteHandler(handler));
        }
        public static void To(this IHttpRouteCommonConfiguratorRoute self, Action<IHttpServerStagedContext> processor)
        {
            self.To(new DelegatedHttpProcessor(processor));
        }
        private class DelegatedHttpRouteHandler : IHttpRouteHandler
        {
            Action<IHttpServerRawContext> _handler;
            public DelegatedHttpRouteHandler(Action<IHttpServerRawContext> handler)
            {
                _handler = handler;
            }
            public void Handle(IHttpServerRawContext context)
            {
                _handler(context);
            }
        }
        private class DelegatedHttpProcessor : IHttpProcessor
        {
            Action<IHttpServerStagedContext> _handler;
            public DelegatedHttpProcessor(Action<IHttpServerStagedContext> handler)
            {
                _handler = handler;
            }
            public void StartProcessing(IHttpServerStagedContext context)
            {
                _handler(context);
            }
        }
    }
}
