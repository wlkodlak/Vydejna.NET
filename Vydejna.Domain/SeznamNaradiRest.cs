using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using System.Net;

namespace Vydejna.Domain
{
    public class SeznamNaradiRest
    {
        private SeznamNaradiService _writeSvc;
        private SeznamNaradiProjection _readSvc;

        public SeznamNaradiRest(SeznamNaradiService writeSvc, SeznamNaradiProjection readSvc)
        {
            _writeSvc = writeSvc;
            _readSvc = readSvc;
        }

        public Task<object> AktivovatNaradi(AktivovatNaradiCommand cmd)
        {
            return HandleCommand(_writeSvc, cmd);
        }

        public Task<object> DeaktivovatNaradi(DeaktivovatNaradiCommand cmd)
        {
            return HandleCommand(_writeSvc, cmd);
        }

        public Task<object> DefinovatNaradi(DefinovatNaradiCommand cmd)
        {
            return HandleCommand(_writeSvc, cmd);
        }

        private static async Task<object> HandleCommand<T>(IHandle<T> handler, T cmd)
        {
            int retry = 0;
            while (retry <= 3)
            {
                try
                {
                    handler.Handle(cmd);
                    return new HttpServerResponseBuilder().WithStatusCode(HttpStatusCode.Accepted).Build();
                }
                catch (ValidationException)
                {
                    return new HttpServerResponseBuilder().WithStatusCode(HttpStatusCode.BadRequest).Build();
                }
                catch
                {
                }
                retry++;
                await Delay(retry);
            }
            return new HttpServerResponseBuilder().WithStatusCode(HttpStatusCode.InternalServerError).Build();
        }

        private static Task Delay(int retry)
        {
            switch (retry)
            {
                case 0:
                    return Task.Delay(1);
                case 1:
                    return Task.Delay(10);
                case 2:
                    return Task.Delay(50);
                case 3:
                    return Task.Delay(200);
                default:
                    return Task.Delay(500);
            }
        }

        public async Task<object> NacistSeznamNaradi(HttpServerRequest request)
        {
            int offset = request.Parameter("offset").AsInteger().Optional(0);
            int pocet = request.Parameter("pocet").AsInteger().Optional(int.MaxValue);
            return await _readSvc.NacistSeznamNaradi(offset, pocet);
        }

        public async Task<object> OveritUnikatnost(HttpServerRequest request)
        {
            string vykres = request.Parameter("vykres").AsString().Mandatory();
            string rozmer = request.Parameter("rozmer").AsString().Mandatory();
            return await _readSvc.OveritUnikatnost(vykres, rozmer);
        }
    }
}
