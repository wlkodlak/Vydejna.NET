using System;
using System.Web;

namespace Vydejna.Web
{
    public class Global : HttpApplication
    {
        private static Program _program;

        protected void Application_Start(object sender, EventArgs e)
        {
            _program = new Program();
            _program.Initialize();
            _program.Start();
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            var app = sender as Global;
            _program.OnBeginRequest(app.Context);
        }

        protected void Application_End(object sender, EventArgs e)
        {
            _program.Stop();
            _program.WaitForExit();
        }
    }
}