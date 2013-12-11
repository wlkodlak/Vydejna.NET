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
        private IWriteSeznamNaradi _writeSvc;
        private IReadSeznamNaradi _readSvc;

        public SeznamNaradiRest(IWriteSeznamNaradi writeSvc, IReadSeznamNaradi readSvc)
        {
            _writeSvc = writeSvc;
            _readSvc = readSvc;
        }

        public Task<AktivovatNaradiCommand> AktivovatNaradi(HttpServerRequest request)
        {
            return TaskResult.GetCompletedTask((AktivovatNaradiCommand)request.PostDataObject);
        }

        public Task<object> AktivovatNaradi(AktivovatNaradiCommand cmd)
        {
            return HandleCommand(_writeSvc, cmd);
        }

        public Task<DeaktivovatNaradiCommand> DeaktivovatNaradi(HttpServerRequest request)
        {
            return TaskResult.GetCompletedTask((DeaktivovatNaradiCommand)request.PostDataObject);
        }

        public Task<object> DeaktivovatNaradi(DeaktivovatNaradiCommand cmd)
        {
            return HandleCommand(_writeSvc, cmd);
        }

        public Task<DefinovatNaradiCommand> DefinovatNaradi(HttpServerRequest request)
        {
            return TaskResult.GetCompletedTask((DefinovatNaradiCommand)request.PostDataObject);
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
                    await handler.Handle(cmd);
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
            return await _readSvc.Handle(new ZiskatSeznamNaradiRequest(offset, pocet));
        }

        public async Task<object> OveritUnikatnost(HttpServerRequest request)
        {
            string vykres = request.Parameter("vykres").AsString().Mandatory();
            string rozmer = request.Parameter("rozmer").AsString().Mandatory();
            return await _readSvc.Handle(new OvereniUnikatnostiRequest(vykres, rozmer));
        }
    }
}
