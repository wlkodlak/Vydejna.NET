using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Vydejna.Web
{
    public class Global : System.Web.HttpApplication
    {
        private static ServiceNaradiHandler _serviceNaradi;

        protected void Application_Start(object sender, EventArgs e)
        {
            var projection = new Domain.SeznamNaradiProjection();
            projection.PridatNaradi(new[] { 
                new Contracts.TypNaradiDto(Guid.NewGuid(), "111-1111", "50x20x5", "", true),
                new Contracts.TypNaradiDto(Guid.NewGuid(), "222-1111", "50x20x5", "", true),
                new Contracts.TypNaradiDto(Guid.NewGuid(), "333-1111", "50x20x5", "", true),
                new Contracts.TypNaradiDto(Guid.NewGuid(), "444-1111", "50x20x5", "", true)
            });
            _serviceNaradi = new ServiceNaradiHandler(projection);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            Context.RemapHandler(new ServiceHttpHandler(_serviceNaradi));
        }
    }
}