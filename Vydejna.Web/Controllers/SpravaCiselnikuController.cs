using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Web.Models;

namespace Vydejna.Web.Controllers
{
    public class SpravaCiselnikuController : Controller
    {
        private ClientApi _clientApi;

        public SpravaCiselnikuController(ClientApi clientApi)
        {
            _clientApi = clientApi;
        }

        public ActionResult Index()
        {
            return View();
        }

        public Task<ActionResult> Dodavatele()
        {
            return _clientApi.Query(new ZiskatSeznamDodavateluRequest()).ContinueWith<ActionResult>(t => View(t.Result));
        }

        public Task<ActionResult> Vady()
        {
            return _clientApi.Query(new ZiskatSeznamVadRequest()).ContinueWith<ActionResult>(t => View(t.Result));
        }

        public Task<ActionResult> Pracoviste(int stranka = 1)
        {
            return _clientApi.Query(new ZiskatSeznamPracovistRequest { Stranka = stranka }).ContinueWith<ActionResult>(t => View(t.Result));
        }
    }
}
