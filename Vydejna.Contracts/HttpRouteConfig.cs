using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vydejna.Contracts
{
    public class HttpRouteConfig
    {
        public IHttpRouteConfigRouteWithDirect Route(string pattern)
        {
            return null;
        }
        public IHttpRouteConfigCommon Common()
        {
            return null;
        }
    }

    public interface IHttpRouteConfigComponents<T>
    {
        T With(IHttpRequestDecoder handler);
        T With(IHttpRequestEncoder handler);
        T With(IHttpRequestEnhancer handler);
        T With(IHttpInputProcessor handler);
        T With(IHttpOutputProcessor handler);
        T With(IHttpPreprocessor handler);
        T With(IHttpPostprocessor handler);
        T Using(IHttpRouteConfigCommon common);
    }

    public interface IHttpRouteConfigRoute : IHttpRouteConfigComponents<IHttpRouteConfigRoute>
    {
        IHttpRouteConfigRoute Clean();
        IHttpRouteConfigComponents To(IHttpProcessor processor);
        IHttpRouteConfigParametrized<T> Parametrized<T>(Func<HttpServerRequest, T> translator);
    }

    public interface IHttpRouteConfigParametrized<T>
    {
        IHttpRouteConfigComponents To(Func<T, object> handler);
    }

    public interface IHttpRouteConfigRouteWithDirect : IHttpRouteConfigRoute
    {
        void To(IHttpRouteHandler directHandler);
    }

    public interface IHttpRouteConfigCommon : IHttpRouteConfigComponents<IHttpRouteConfigCommon>
    {
    }

    public interface IHttpRouteConfigComponents : IHttpRouteConfigComponents<IHttpRouteConfigComponents>
    {
        IHttpRouteConfigComponents Clean();
    }
}
