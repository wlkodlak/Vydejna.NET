using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using ServiceLib;
using Vydejna.Web.Models;
using Vydejna.Contracts;

namespace Vydejna.Web.Controllers
{
    public class SeznamNaradiController : Controller
    {
        private ClientApi _api;

        public SeznamNaradiController(ClientApi api)
        {
            _api = api;
        }

        public Task<ActionResult> Prehled(int stranka = 1)
        {
            return _api.Query(new PrehledNaradiRequest { Stranka = stranka }).ContinueWith<ActionResult>(task => View(task.Result));
        }
    }
}