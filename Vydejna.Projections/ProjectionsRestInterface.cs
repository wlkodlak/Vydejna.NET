using ServiceLib;
using System;
using System.Threading.Tasks;
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

        public Task DetailNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new DetailNaradiRequest();
                request.NaradiId = ctx.Parameter("naradiid").AsGuid().Mandatory().Get();
                return _bus.SendQuery<DetailNaradiRequest, DetailNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task NajitObjednavku(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new NajitObjednavkuRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                return _bus.SendQuery<NajitObjednavkuRequest, NajitObjednavkuResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task NajitDodaciList(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new NajitDodaciListRequest();
                request.DodaciList = ctx.Parameter("dodacilist").AsString().Mandatory().Get();
                return _bus.SendQuery<NajitDodaciListRequest, NajitDodaciListResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task NaradiNaObjednavce(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaObjednavceRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                request.KodDodavatele = ctx.Parameter("dodavatel").AsString().Mandatory().Get();
                return _bus.SendQuery<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task NaradiNaPracovisti(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaPracovistiRequest();
                request.KodPracoviste = ctx.Parameter("pracoviste").AsString().Mandatory().Get();
                return _bus.SendQuery<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task NaradiNaVydejne(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatNaradiNaVydejneRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task PrehledNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<PrehledNaradiRequest, PrehledNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task PrehledCislovaneho(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledCislovanehoNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task PrehledObjednavek(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new PrehledObjednavekRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                request.Razeni = ctx.Parameter("razeni").AsEnum<PrehledObjednavekRazeni>().Default(PrehledObjednavekRazeni.PodleCislaObjednavky).Get();
                return _bus.SendQuery<PrehledObjednavekRequest, PrehledObjednavekResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task OveritUnikatnost(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new OvereniUnikatnostiRequest();
                request.Vykres = ctx.Parameter("vykres").AsString().Mandatory().Get();
                request.Rozmer = ctx.Parameter("rozmer").AsString().Mandatory().Get();
                return _bus.SendQuery<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task SeznamDodavatelu(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamDodavateluRequest();
                return _bus.SendQuery<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task SeznamNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task SeznamPracovist(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamPracovistRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        public Task SeznamVad(IHttpServerStagedContext ctx)
        {
            try
            {
                var request = new ZiskatSeznamVadRequest();
                return _bus.SendQuery<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return TaskUtils.FromError<object>(ex);
            }
        }

        private void Finish<T>(Task<T> task, IHttpServerStagedContext ctx)
        {
            if (task.Exception == null)
            {
                ctx.StatusCode = 200;
                ctx.OutputString = ctx.OutputSerializer.Serialize(task.Result);
                ctx.OutputHeaders.ContentType = ctx.OutputSerializer.ContentType;
            }
            else
            {
                ctx.StatusCode = 400;
                ctx.OutputString = task.Exception.InnerException.Message;
                ctx.OutputHeaders.ContentType = "text/plain";
            }
        }
    }
}
