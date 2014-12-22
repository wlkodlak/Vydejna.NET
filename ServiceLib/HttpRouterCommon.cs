using System.Collections.Generic;
using System.Linq;

namespace ServiceLib
{
    public class HttpRouterCommon : IHttpRouteCommonConfiguratorExtended
    {
        private string _prefix;
        private readonly IHttpAddRoute _router;
        private readonly List<IHttpRouteConfigCommitable> _subRouters;
        private readonly List<IHttpRouteConfigCommitable> _pending;
        private ISerializerPicker _picker;
        private readonly IList<IHttpSerializer> _serializers;

        public HttpRouterCommon(IHttpAddRoute router)
        {
            _prefix = "";
            _router = router;
            _subRouters = new List<IHttpRouteConfigCommitable>();
            _pending = new List<IHttpRouteConfigCommitable>();
            _picker = null;
            _serializers = new List<IHttpSerializer>();
        }

        public HttpRouterCommon(IHttpRouteCommonConfiguratorExtended parent, string path)
        {
            parent.Register(this);
            _prefix = parent.GetPath();
            if (!string.IsNullOrEmpty(path))
                _prefix = string.Concat(_prefix, "/", path);
            _router = parent.GetRouter();
            _subRouters = new List<IHttpRouteConfigCommitable>();
            _pending = new List<IHttpRouteConfigCommitable>();
            _picker = parent.GetPicker();
            _serializers = parent.GetSerializers().ToList();
        }

        void IHttpRouteCommonConfiguratorExtended.Register(IHttpRouteConfigCommitable subRouter)
        {
            _subRouters.Add(subRouter);
        }

        IHttpAddRoute IHttpRouteCommonConfiguratorExtended.GetRouter()
        {
            return _router;
        }

        string IHttpRouteCommonConfiguratorExtended.GetPath()
        {
            return _prefix;
        }

        ISerializerPicker IHttpRouteCommonConfiguratorExtended.GetPicker()
        {
            return _picker;
        }

        IList<IHttpSerializer> IHttpRouteCommonConfiguratorExtended.GetSerializers()
        {
            return _serializers;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.SubRouter()
        {
            return new HttpRouterCommon(this, "");
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.ForFolder(string path)
        {
            _prefix = string.Concat(_prefix, "/", path);
            return this;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.WithPicker(ISerializerPicker picker)
        {
            if (_picker == null)
                _picker = picker;
            else
                _picker = new HttpSerializerPickerProxy(picker, _picker);
            return this;
        }

        IHttpRouteCommonConfigurator IHttpRouteCommonConfigurator.WithSerializer(IHttpSerializer serializer)
        {
            _serializers.Add(serializer);
            return this;
        }

        void IHttpRouteConfigCommitable.Commit()
        {
            foreach (var item in _subRouters)
                item.Commit();
            foreach (var item in _pending)
                item.Commit();
            _pending.Clear();
        }

        IHttpRouteCommonConfiguratorRoute IHttpRouteCommonConfigurator.Route(string path)
        {
            return new HttpRouterCommonRoute(this, path);
        }

        private class HttpRouterCommonRoute : IHttpRouteCommonConfiguratorRoute, IHttpRouteConfigCommitable
        {
            private readonly HttpRouterCommon _parent;
            private bool _isCommitted;
            private readonly string _path;
            private readonly IHttpAddRoute _router;
            private ISerializerPicker _picker;
            private readonly IList<IHttpSerializer> _serializers;
            private IHttpRouteHandler _routeHandler;

            public HttpRouterCommonRoute(HttpRouterCommon parent, string path)
            {
                _parent = parent;
                _router = parent._router;
                _picker = parent._picker;
                _serializers = parent._serializers.ToList();
                _path = string.IsNullOrEmpty(path) ? parent._prefix : string.Concat(parent._prefix, "/", path);
            }

            public IHttpRouteCommonConfiguratorRoute WithPicker(ISerializerPicker picker)
            {
                if (_picker == null)
                    _picker = picker;
                else
                    _picker = new HttpSerializerPickerProxy(picker, _picker);
                return this;
            }

            public IHttpRouteCommonConfiguratorRoute WithSerializer(IHttpSerializer serializer)
            {
                _serializers.Add(serializer);
                return this;
            }

            public IHttpRouteConfigCommitable To(IHttpRouteHandler handler)
            {
                _routeHandler = handler;
                _parent._pending.Add(this);
                return this;
            }

            public IHttpRouteConfigCommitable To(IHttpProcessor processor)
            {
                _routeHandler = new HttpRouteStagedHandler(_picker, _serializers.ToList(), processor);
                _parent._pending.Add(this);
                return this;
            }

            public void Commit()
            {
                if (_isCommitted)
                    return;
                _isCommitted = true;
                _router.AddRoute(_path, _routeHandler);
            }
        }
    }
}