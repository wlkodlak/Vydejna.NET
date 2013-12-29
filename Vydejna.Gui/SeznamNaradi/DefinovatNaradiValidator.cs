using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vydejna.Contracts;
using Vydejna.Gui.Common;

namespace Vydejna.Gui.SeznamNaradi
{
    public interface IDefinovatNaradiValidator
    {
        void Zkontrolovat(DefinovatNaradiValidace definovatNaradiValidace);
    }

    public class DefinovatNaradiValidace
    {
        public string Vykres { get; set; }
        public string Rozmer { get; set; }
        public string Druh { get; set; }
    }

    public class DefinovatNaradiValidator : IDefinovatNaradiValidator
    {
        private IEventPublisher _bus;
        private IReadSeznamNaradi _readSvc;
        private SerializingOnlineQuery<Validace> _query;

        private class Validace
        {
            public DefinovatNaradiValidace Vstup;
            public UiMessages.ValidovanoDefinovatNaradi Vysledek;

            public Validace(DefinovatNaradiValidace vstup)
            {
                this.Vstup = vstup;
                this.Vysledek = new UiMessages.ValidovanoDefinovatNaradi();
            }
        }

        public DefinovatNaradiValidator(IEventPublisher vysledky, IReadSeznamNaradi readSvc)
        {
            this._bus = vysledky;
            this._readSvc = readSvc;
            this._query = new SerializingOnlineQuery<Validace>(ZkontrolovatPrazdnaPole, ZkontrolovatUnikatnost, OdeslatVysledky, true);
        }

        public void Zkontrolovat(DefinovatNaradiValidace vstup)
        {
            _query.Run(new Validace(vstup));
        }

        private bool ZkontrolovatPrazdnaPole(Validace validace)
        {
            if (string.IsNullOrEmpty(validace.Vstup.Vykres))
                validace.Vysledek.Chyba("Vykres", "Výkres je nutné zadat");
            if (string.IsNullOrEmpty(validace.Vstup.Rozmer))
                validace.Vysledek.Chyba("Rozmer", "Rozměr je nutné zadat");
            return validace.Vysledek.Chyby.Count != 0;
        }

        private void ZkontrolovatUnikatnost(Validace validace, Action onCompleted)
        {
            new ZkontrolovatUnikatnostWorker(_readSvc, validace, onCompleted).Execute();
        }

        private class ZkontrolovatUnikatnostWorker
        {
            private IReadSeznamNaradi _readSvc;
            private Validace _validace;
            private Action _onCompleted;

            public ZkontrolovatUnikatnostWorker(IReadSeznamNaradi readSvc, Validace validace, Action onCompleted)
            {
                this._readSvc = readSvc;
                this._validace = validace;
                this._onCompleted = onCompleted;
            }

            public void Execute()
            {
                _readSvc.Handle(new QueryExecution<OvereniUnikatnostiRequest, OvereniUnikatnostiResponse>(
                    new OvereniUnikatnostiRequest(_validace.Vstup.Vykres, _validace.Vstup.Rozmer),
                    ValidaceDokoncena, ChybaValidace));
            }

            private void ValidaceDokoncena(OvereniUnikatnostiResponse overeni)
            {
                if (overeni.Existuje)
                {
                    _validace.Vysledek.Chyba("Vykres", "Kombinace výkresu a rozměru musí být unikátní");
                    _validace.Vysledek.Chyba("Rozmer", "Kombinace výkresu a rozměru musí být unikátní");
                }
                _onCompleted();
            }

            private void ChybaValidace(Exception obj)
            {
                _onCompleted();
            }
        }

        private void OdeslatVysledky(Validace validace)
        {
            _bus.Publish(validace.Vysledek);
        }
    }
}
