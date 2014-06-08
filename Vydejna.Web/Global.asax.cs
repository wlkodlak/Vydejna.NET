using System;
using System.Web;
using System.Web.Routing;
using System.Web.Mvc;
using System.Web.SessionState;
using Vydejna.Web.Controllers;

namespace Vydejna.Web
{
    public class Global : HttpApplication
    {
        private static Program _program;

        protected void Application_Start(object sender, EventArgs e)
        {
            var useEmbeddedServer = System.Configuration.ConfigurationManager.AppSettings["useEmbeddedServer"] != "false";
            if (useEmbeddedServer)
            {
                _program = new Program();
                _program.Initialize("Api");
                _program.Start();
            }
            AddRoutes(RouteTable.Routes);
            AddGlobalFilters(GlobalFilters.Filters);
            AddControllerFactory(ControllerBuilder.Current);
        }

        private void AddControllerFactory(ControllerBuilder builder)
        {
            builder.SetControllerFactory(new ControllerFactory());
        }

        private void AddGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }

        private void AddRoutes(RouteCollection routes)
        {
            if (_program != null)
                _program.AddWebRoute(routes);
            routes.IgnoreRoute("{resource}.axd/{*path}");
            routes.MapRoute("Mvc", "{controller}/{action}/{id}",
                new { controller = "Home", action = "Index", id = UrlParameter.Optional },
                new[] { "Vydejna.Web.Controllers" });
        }

        protected void Application_End(object sender, EventArgs e)
        {
            _program.Stop();
            _program.WaitForExit();
        }

        private class ControllerFactory : IControllerFactory
        {
            private Models.ClientApi _clientApi;

            public ControllerFactory()
            {
                var apiUrl = System.Configuration.ConfigurationManager.AppSettings.Get("api");
                _clientApi = new Models.ClientApi(apiUrl);
            }

            public IController CreateController(RequestContext requestContext, string controllerName)
            {
                switch (controllerName)
                {
                    case "Home":
                        return new HomeController();
                    case "SeznamNaradi":
                        return new SeznamNaradiController(_clientApi);
                    case "SpravaCiselniku":
                        return new SpravaCiselnikuController(_clientApi);
                    default:
                        return null;
                }
            }

            public SessionStateBehavior GetControllerSessionBehavior(RequestContext requestContext, string controllerName)
            {
                return SessionStateBehavior.Default;
            }

            public void ReleaseController(IController controller)
            {
                var disposable = controller as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
        }
    }
}