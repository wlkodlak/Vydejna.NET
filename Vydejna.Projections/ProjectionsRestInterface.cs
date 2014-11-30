using ServiceLib;
using System;
using System.Globalization;
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
            config.Route("seznamnaradi/historie").To(HistorieNaradiVsechno);

            config.Route("cislovane/prehled").To(PrehledCislovaneho);
            config.Route("cislovane/historie").To(HistorieNaradiCislovane);

            config.Route("objednavky/najit").To(NajitObjednavku);
            config.Route("objednavky/dodacilist").To(NajitDodaciList);
            config.Route("objednavky/naradi").To(NaradiNaObjednavce);
            config.Route("objednavky/prehled").To(PrehledObjednavek);
            config.Route("objednavky/historie").To(HistorieNaradiObjednavky);

            config.Route("pracoviste/seznam").To(SeznamPracovist);
            config.Route("pracoviste/naradi").To(NaradiNaPracovisti);
            config.Route("pracoviste/historie").To(HistorieNaradiPracoviste);

            config.Route("dodavatele/seznam").To(SeznamDodavatelu);

            config.Route("vady/seznam").To(SeznamVad);
        }

        public Task SeznamNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/vsechno
                var request = new ZiskatSeznamNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatSeznamNaradiRequest, ZiskatSeznamNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task DetailNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/detail
                var request = new DetailNaradiRequest();
                request.NaradiId = ctx.Parameter("naradiid").AsGuid().Mandatory().Get();
                return _bus.SendQuery<DetailNaradiRequest, DetailNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task PrehledNaradi(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/prehled
                var request = new PrehledNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<PrehledNaradiRequest, PrehledNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task NaradiNaVydejne(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/navydejne
                var request = new ZiskatNaradiNaVydejneRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatNaradiNaVydejneRequest, ZiskatNaradiNaVydejneResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task OveritUnikatnost(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/overitunikatnost
                var request = new OvereniUnikatnostiRequest();
                request.Vykres = ctx.Parameter("vykres").AsString().Mandatory().Get();
                request.Rozmer = ctx.Parameter("rozmer").AsString().Mandatory().Get();
                return _bus.SendQuery<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task PrehledCislovaneho(IHttpServerStagedContext ctx)
        {
            try
            {
                // cislovane/prehled
                var request = new PrehledCislovanehoNaradiRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<PrehledCislovanehoNaradiRequest, PrehledCislovanehoNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task NajitObjednavku(IHttpServerStagedContext ctx)
        {
            try
            {
                // objednavky/najit
                var request = new NajitObjednavkuRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                return _bus.SendQuery<NajitObjednavkuRequest, NajitObjednavkuResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task NajitDodaciList(IHttpServerStagedContext ctx)
        {
            try
            {
                // objednavky/dodacilist
                var request = new NajitDodaciListRequest();
                request.DodaciList = ctx.Parameter("dodacilist").AsString().Mandatory().Get();
                return _bus.SendQuery<NajitDodaciListRequest, NajitDodaciListResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task NaradiNaObjednavce(IHttpServerStagedContext ctx)
        {
            try
            {
                // objednavky/naradi
                var request = new ZiskatNaradiNaObjednavceRequest();
                request.Objednavka = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                request.KodDodavatele = ctx.Parameter("dodavatel").AsString().Mandatory().Get();
                return _bus.SendQuery<ZiskatNaradiNaObjednavceRequest, ZiskatNaradiNaObjednavceResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task PrehledObjednavek(IHttpServerStagedContext ctx)
        {
            try
            {
                // objednavky/prehled
                var request = new PrehledObjednavekRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                request.Razeni = ctx.Parameter("razeni").AsEnum<PrehledObjednavekRazeni>().Default(PrehledObjednavekRazeni.PodleCislaObjednavky).Get();
                return _bus.SendQuery<PrehledObjednavekRequest, PrehledObjednavekResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task SeznamPracovist(IHttpServerStagedContext ctx)
        {
            try
            {
                // pracoviste/seznam
                var request = new ZiskatSeznamPracovistRequest();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<ZiskatSeznamPracovistRequest, ZiskatSeznamPracovistResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task NaradiNaPracovisti(IHttpServerStagedContext ctx)
        {
            try
            {
                // pracoviste/naradi
                var request = new ZiskatNaradiNaPracovistiRequest();
                request.KodPracoviste = ctx.Parameter("pracoviste").AsString().Mandatory().Get();
                return _bus.SendQuery<ZiskatNaradiNaPracovistiRequest, ZiskatNaradiNaPracovistiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task SeznamDodavatelu(IHttpServerStagedContext ctx)
        {
            try
            {
                // dodavatele/seznam
                var request = new ZiskatSeznamDodavateluRequest();
                return _bus.SendQuery<ZiskatSeznamDodavateluRequest, ZiskatSeznamDodavateluResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task SeznamVad(IHttpServerStagedContext ctx)
        {
            try
            {
                // vady/seznam
                var request = new ZiskatSeznamVadRequest();
                return _bus.SendQuery<ZiskatSeznamVadRequest, ZiskatSeznamVadResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task HistorieNaradiVsechno(IHttpServerStagedContext ctx)
        {
            try
            {
                // seznamnaradi/historie
                var request = new HistorieNaradiRequest();
                request.TypFiltru = HistorieNaradiTypFiltru.Vsechno;
                request.DatumOd = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MinValue).Get();
                request.DatumDo = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MaxValue).Get();
                request.PouzeVydejeDoVyroby = ctx.Parameter("jenvydeje").AsString().Default("false").Get() == "true";
                request.NaradiId = ctx.Parameter("naradi").AsGuid().Default(Guid.Empty).Get();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                if (request.NaradiId.HasValue && request.NaradiId.Value != Guid.Empty)
                    request.TypFiltru = HistorieNaradiTypFiltru.Naradi;
                return _bus.SendQuery<HistorieNaradiRequest, HistorieNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task HistorieNaradiCislovane(IHttpServerStagedContext ctx)
        {
            try
            {
                // cislovane/historie
                var request = new HistorieNaradiRequest();
                request.TypFiltru = HistorieNaradiTypFiltru.CislovaneNaradi;
                request.CisloNaradi = ctx.Parameter("cisloNaradi").AsInteger().Mandatory().Get();
                request.DatumOd = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MinValue).Get();
                request.DatumDo = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MaxValue).Get();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<HistorieNaradiRequest, HistorieNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task HistorieNaradiPracoviste(IHttpServerStagedContext ctx)
        {
            try
            {
                // historie/seznam
                var request = new HistorieNaradiRequest();
                request.TypFiltru = HistorieNaradiTypFiltru.Pracoviste;
                request.DatumOd = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MinValue).Get();
                request.DatumDo = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MaxValue).Get();
                request.KodPracoviste = ctx.Parameter("pracoviste").AsString().Mandatory().Get();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<HistorieNaradiRequest, HistorieNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        public Task HistorieNaradiObjednavky(IHttpServerStagedContext ctx)
        {
            try
            {
                // objednavky/historie
                var request = new HistorieNaradiRequest();
                request.TypFiltru = HistorieNaradiTypFiltru.Objednavka;
                request.DatumOd = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MinValue).Get();
                request.DatumDo = ctx.Parameter("datumod").As(ParseDate).Default(DateTime.MaxValue).Get();
                request.KodDodavatele = ctx.Parameter("dodavatel").AsString().Mandatory().Get();
                request.CisloObjednavky = ctx.Parameter("objednavka").AsString().Mandatory().Get();
                request.Stranka = ctx.Parameter("stranka").AsInteger().Default(1).Get();
                return _bus.SendQuery<HistorieNaradiRequest, HistorieNaradiResponse>(request).ContinueWith(task => Finish(task, ctx));
            }
            catch (Exception ex)
            {
                return ProcessException(ex, ctx);
            }
        }

        private DateTime ParseDate(string input)
        {
            return DateTime.ParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture);
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

        private Task ProcessException(Exception exception, IHttpServerStagedContext ctx)
        {
            ctx.StatusCode = 400;
            ctx.OutputString = exception.Message;
            ctx.OutputHeaders.ContentType = "text/plain";
            return TaskUtils.CompletedTask();
        }
    }
}
