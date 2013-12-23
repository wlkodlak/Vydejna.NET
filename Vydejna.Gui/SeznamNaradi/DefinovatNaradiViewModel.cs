using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Vydejna.Contracts;
using Vydejna.Gui.Common;

namespace Vydejna.Gui.SeznamNaradi
{
    public class DefinovatNaradiViewModel : ViewModelBase
        , IHandleSync<UiMessages.ValidovanoDefinovatNaradi>
        , IHandleSync<UiMessages.DokoncenaDefiniceOtevrena>
    {
        private IDefinovatNaradiValidator _validator;
        private IWriteSeznamNaradi _writeSeznamNaradi;
        private IEventPublisher _bus;
        private SimpleCommand _definovatNaradiCommand;

        private string _vykres;
        private string _rozmer;
        private string _druh;
        private bool _povolenyUpravy;

        public DefinovatNaradiViewModel(IDefinovatNaradiValidator validator, IWriteSeznamNaradi writeSeznamNaradi, IEventPublisher bus)
        {
            this._validator = validator;
            this._writeSeznamNaradi = writeSeznamNaradi;
            this._bus = bus;
            this._definovatNaradiCommand = new SimpleCommand(DefinovatNaradi);
            this._vykres = "";
            this._rozmer = "";
            this._druh = "";
            this._definovatNaradiCommand.Enabled = false;
            this._povolenyUpravy = true;
        }

        private async void DefinovatNaradi()
        {
            PovolenyUpravy = false;
            _definovatNaradiCommand.Enabled = false;
            var cmd = new DefinovatNaradiCommand
            {
                Vykres = _vykres,
                Rozmer = _rozmer,
                Druh = _druh
            };
            await _writeSeznamNaradi.Handle(cmd).ConfigureAwait(false);
            _bus.Publish(new UiMessages.DokoncenaDefiniceNaradi());
        }

        private void VyvolatValidaci()
        {
            var validace = new DefinovatNaradiValidace() { Vykres = _vykres, Rozmer = _rozmer, Druh = _druh };
            _validator.Zkontrolovat(validace);
        }

        public string Vykres
        {
            get { return _vykres; }
            set
            {
                _vykres = value;
                VyvolatValidaci();
                RaisePropertyChanged("Vykres");
            }
        }

        public string Rozmer
        {
            get { return _rozmer; }
            set
            {
                _rozmer = value;
                VyvolatValidaci();
                RaisePropertyChanged("Rozmer");
            }
        }

        public string Druh
        {
            get { return _druh; }
            set
            {
                _druh = value;
                VyvolatValidaci();
                RaisePropertyChanged("Druh");
            }
        }

        public bool PovolenyUpravy
        {
            get { return _povolenyUpravy; }
            set { SetProperty("PovolenyUpravy", ref _povolenyUpravy, value); }
        }

        public ICommand DefinovatNaradiCommand { get { return _definovatNaradiCommand; } }

        public void Handle(UiMessages.ValidovanoDefinovatNaradi evt)
        {
            ClearErrors(null);
            foreach (var chyba in evt.Chyby)
                AddError(chyba.Polozka, chyba.Chyba);
            _definovatNaradiCommand.Enabled = !HasErrors;
            RaisePropertyChanged(null);
        }

        public void Handle(UiMessages.DokoncenaDefiniceOtevrena message)
        {
            VyvolatValidaci();
        }
    }
}
