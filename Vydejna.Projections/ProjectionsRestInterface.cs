using ServiceLib;
using System;
using Vydejna.Contracts;

namespace Vydejna.Projections
{
    public class ProjectionsRestInterface
    {
        private IPublisher _bus;

        public ProjectionsRestInterface(IPublisher bus)
        {
            _bus = bus;
        }

        public void Register(IHttpRouteCommonConfigurator config)
        {
            config.Route("seznamnaradi/vsechno").To(SeznamNaradi);
            config.Route("seznamnaradi/detail").To(DetailNaradi);
            config.Route("seznamnaradi/prehled").To(PrehledNaradi);
            config.Route("seznamnaradi/navydejne").To(NaradiNaVydejne);
            config.Route("seznamnaradi/overitunikatnost").To(OveritUnikatnost);

            config.Route("cislovane/prehled").To(PrehledCislovaneho);

            config.Route("objednavky/najit").To(NajitObjednavku);
            config.Route("objednavky/dodacilist").To(NajitDodaciList);
            config.Route("objednavky/naradi").To(NaradiNaObjednavce);
            config.Route("objednavky/prehled").To(PrehledObjednavek);

            config.Route("pracoviste/seznam").To(SeznamPracovist);
            config.Route("pracoviste/naradi").To(NaradiNaPracovisti);

            config.Route("dodavatele/seznam").To(SeznamDodavatelu);

            config.Route("vady/seznam").To(SeznamVad);
        }

        public void DetailNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new DetailNaradiRequest();
                request.NaradiId = ctx.Parameter("naradiid").AsGuid().Mandatory().Get();
                _bus.Publish(new QueryExecution<DetailNaradiRequest, DetailNaradiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void NajitObjednavku(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new NajitObjednavkuRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                _bus.Publish(new QueryExecution<NajitObjednavkuRequest, NajitObjednavkuResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void NajitDodaciList(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new NajitDodaciListRequest();
                request.DodaciList = ctx.Parameter("dodacilist").AsString().Mandatory().Get();
                _bus.Publish(new QueryExecution<NajitDodaciListRequest, NajitDodaciListResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void NaradiNaObjednavce(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaObjednavceRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                request.KodDodavatele = ctx.Parameter("dodavatel").AsString().Mandatory().Get();
                _bus.Publish(new QueryExecution<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void NaradiNaPracovisti(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaPracovistiRequest();
                request.KodPracoviste = ctx.Parameter("pracoviste").AsString().Mandatory().Get();
                _bus.Publish(new QueryExecution<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void NaradiNaVydejne(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaVydejneRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                _bus.Publish(new QueryExecution<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void PrehledNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                _bus.Publish(new QueryExecution<PrehledNaradiRequest, PrehledNaradiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void PrehledCislovaneho(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledCislovanehoNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                _bus.Publish(new QueryExecution<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void PrehledObjednavek(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledObjednavekRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                request.Razeni = ctx.Parameter("razeni").AsEnum<PrehledObjednavekRazeni>().Default(PrehledObjednavekRazeni.PodleCislaObjednavky).Get();
                _bus.Publish(new QueryExecution<PrehledObjednavekRequest, PrehledObjednavekResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void OveritUnikatnost(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new OvereniUnikatnostiRequest();
                request.Vykres = ctx.Parameter("vykres").AsString().Mandatory().Get();
                request.Rozmer = ctx.Parameter("rozmer").AsString().Mandatory().Get();
                _bus.Publish(new QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void SeznamDodavatelu(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamDodavateluRequest();
                _bus.Publish(new QueryExecution<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void SeznamNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                _bus.Publish(new QueryExecution<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void SeznamPracovist(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamPracovistRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                _bus.Publish(new QueryExecution<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        public void SeznamVad(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamVadRequest();
                _bus.Publish(new QueryExecution<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>(request, res => SendResponse(ctx, res), ex => SendError(ctx, ex)));
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        private void SendResponse<T>(IHttpServerStagedContext ctx, T response)
        {
            ctx.StatusCode = 200;
            ctx.OutputString = ctx.OutputSerializer.Serialize(response);
            ctx.OutputHeaders.ContentType = ctx.OutputSerializer.ContentType;
            ctx.Close();
        }

        private void SendError(IHttpServerStagedContext ctx, Exception ex)
        {
            ctx.StatusCode = 400;
            ctx.OutputString = ex.Message;
            ctx.OutputHeaders.ContentType = "text/plain";
            ctx.Close();
        }
    }
}
