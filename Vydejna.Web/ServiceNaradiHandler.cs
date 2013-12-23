using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Vydejna.Contracts;
using Vydejna.Domain;

namespace Vydejna.Web
{
    public class ServiceNaradiHandler
    {
        private IReadSeznamNaradi _readSvc;

        public ServiceNaradiHandler(IReadSeznamNaradi readSvc)
        {
            _readSvc = readSvc;
        }

        public SeznamNaradiDto ZiskatNaradi(ZiskatNaradiRequest request)
        {
            var task = _readSvc.NacistSeznamNaradi(request.Offset, request.MaxPocet);
            return task.Result;
        }

        public OvereniUnikatnostiDto OveritUnikatnost(OveritUnikatnostRequest request)
        {
            var task = _readSvc.OveritUnikatnost(request.Vykres, request.Rozmer);
            return task.Result;
        }

        public ZiskatNaradiRequest ZiskatNaradiParameters(IRequestParameters request)
        {
            var param = new ZiskatNaradiRequest();
            param.Offset = request.Get("index").AsInteger().WithDefault(0);
            param.MaxPocet = request.Get("pocet").AsInteger().WithDefault(int.MaxValue);
            return param;
        }
    }

    public class ZiskatNaradiRequest
    {
        public int Offset;
        public int MaxPocet;
    }

    public class OveritUnikatnostRequest
    {
        public string Vykres;
        public string Rozmer;
    }
}