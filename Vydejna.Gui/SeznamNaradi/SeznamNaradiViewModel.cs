using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Vydejna.Contracts;
using Vydejna.Gui.Common;
using Vydejna.Gui.Shell;

namespace Vydejna.Gui.SeznamNaradi
{
    public class SeznamNaradiViewModel
        : IHandleSync<UiMessages.SeznamNaradiOtevren>
        , IHandleSync<UiMessages.DokoncenaDefiniceNaradi>
    {
        private IShell _shell;
        private IReadSeznamNaradi _readSvc;
        private IWriteSeznamNaradi _writeSvc;
        private SimpleCommand _definovatNaradiCommand;
        private string _hledanyText = "";
        private ListCollectionView _seznamNaradi;
        private ObservableCollection<TypNaradiViewModel> _nactenaNaradi;
        private SimpleCommand _aktivovatNaradiCommand;
        private SimpleCommand _deaktivovatNaradiCommand;
        private Func<DefinovatNaradiViewModel> _createDefinovatNaradi;

        public SeznamNaradiViewModel(
            IShell shell, 
            IReadSeznamNaradi readSeznamNaradi, 
            IWriteSeznamNaradi writeSeznamNaradi, 
            Func<DefinovatNaradiViewModel> createDefinovatNaradi)
        {
            this._shell = shell;
            this._readSvc = readSeznamNaradi;
            this._writeSvc = writeSeznamNaradi;
            this._createDefinovatNaradi = createDefinovatNaradi;
            this._definovatNaradiCommand = new SimpleCommand(DefinovatNaradi);
            this._nactenaNaradi = new ObservableCollection<TypNaradiViewModel>();
            this._seznamNaradi = new ListCollectionView(_nactenaNaradi);
            this._aktivovatNaradiCommand = new SimpleCommand(AktivovatNaradi);
            this._deaktivovatNaradiCommand = new SimpleCommand(DeaktivovatNaradi);
        }

        private SeznamNaradiViewModel()
        {
            this._definovatNaradiCommand = new SimpleCommand(DefinovatNaradi);
            this._nactenaNaradi = new ObservableCollection<TypNaradiViewModel>();
            this._seznamNaradi = new ListCollectionView(_nactenaNaradi);
            this._aktivovatNaradiCommand = new SimpleCommand(AktivovatNaradi);
            this._deaktivovatNaradiCommand = new SimpleCommand(DeaktivovatNaradi);
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "1-22-221", Rozmer = "o 140mm", Druh = "Kleština", Aktivni = false });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "2-224-154", Rozmer = "500x100mm", Druh = "Brusný kotouč", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "24-224-1112", Rozmer = "o 20mm", Druh = "Kleština", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "224-2101-1b", Rozmer = "120x5mm", Druh = "", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "471-1144-2b", Rozmer = "66x140mm", Druh = "", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "5-5574-2240", Rozmer = "", Druh = "Kleština", Aktivni = false });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "5-4472-247b", Rozmer = "", Druh = "Brusný kotouč", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "547-5574-2b", Rozmer = "50x20x5mm", Druh = "Brusný kotouč", Aktivni = true });
            this._nactenaNaradi.Add(new TypNaradiViewModel { Vykres = "8724-5521",   Rozmer = "800x50mm", Druh = "Brusný kotouč", Aktivni = true });
        }

        public static SeznamNaradiViewModel DesignerVM { get { return new SeznamNaradiViewModel(); } }

        private void DefinovatNaradi()
        {
            _shell.RunModule(_createDefinovatNaradi());
        }

        private void AktivovatNaradi(object parameter)
        {
            var naradi = (TypNaradiViewModel)parameter;
            if (naradi.Aktivni)
                return;
            var cmd = new AktivovatNaradiCommand { NaradiId = naradi.Id };
            _writeSvc.AktivovatNaradi(cmd);
            naradi.Aktivni = true;
        }

        private void DeaktivovatNaradi(object parameter)
        {
            var naradi = (TypNaradiViewModel)parameter;
            if (!naradi.Aktivni)
                return;
            var cmd = new DeaktivovatNaradiCommand { NaradiId = naradi.Id };
            _writeSvc.DeaktivovatNaradi(cmd);
            naradi.Aktivni = false;
        }

        public ICommand DefinovatNaradiCommand { get { return _definovatNaradiCommand; } }
        public ICommand AktivovatNaradiCommand { get { return _aktivovatNaradiCommand; } }
        public ICommand DeaktivovatNaradiCommand { get { return _deaktivovatNaradiCommand; } }

        public void Handle(UiMessages.SeznamNaradiOtevren evt)
        {
            var task = NacistSeznamNaradi();
        }

        public void Handle(UiMessages.DokoncenaDefiniceNaradi evt)
        {
            var task = NacistSeznamNaradi();
        }

        public void Handle(UiMessages.NactenSeznamNaradi evt)
        {
            var seznam = evt.SeznamNaradiDto;
            _nactenaNaradi.Clear();
            foreach (var naradi in seznam.SeznamNaradi)
            {
                var typ = new TypNaradiViewModel()
                {
                    Id = naradi.Id,
                    Vykres = naradi.Vykres,
                    Rozmer = naradi.Rozmer,
                    Druh = naradi.Druh,
                    Aktivni = naradi.Aktivni
                };
                _nactenaNaradi.Add(typ);
            }
        }

        private async Task NacistSeznamNaradi()
        {
            var seznam = await _readSvc.NacistSeznamNaradi(0, int.MaxValue);
            Handle(new UiMessages.NactenSeznamNaradi(seznam));
        }

        public ListCollectionView SeznamNaradi
        {
            get { return _seznamNaradi; }
        }

        public string HledanyText
        {
            get { return _hledanyText; }
            set
            {
                _hledanyText = value ?? "";
                _seznamNaradi.Filter = FiltrNaradi;
            }
        }

        private bool FiltrNaradi(object obj)
        {
            var naradi = obj as TypNaradiViewModel;
            return naradi != null && naradi.Vykres.StartsWith(_hledanyText);
        }
    }

    public class TypNaradiViewModel : ViewModelBase
    {
        private Guid _id;
        private string _vykres;
        private string _rozmer;
        private string _druh;
        private bool _aktivni;

        public Guid Id
        {
            get { return _id; }
            set { SetProperty("Id", ref _id, value); }
        }

        public string Vykres
        {
            get { return _vykres; }
            set { SetProperty("Vykres", ref _vykres, value); }
        }

        public string Rozmer
        {
            get { return _rozmer; }
            set { SetProperty("Rozmer", ref _rozmer, value); }
        }

        public string Druh
        {
            get { return _druh; }
            set { SetProperty("Druh", ref _druh, value); }
        }

        public bool Aktivni
        {
            get { return _aktivni; }
            set { SetProperty("Aktivni", ref _aktivni, value); }
        }
    }
}
