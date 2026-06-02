using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class VermoegenViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<VermoegenDepot> Depots { get; } = new();
        public ObservableCollection<VermoegenPositionRow> Positionen { get; } = new();

        private VermoegenDepot? _selectedDepot;
        public VermoegenDepot? SelectedDepot
        {
            get => _selectedDepot;
            set
            {
                _selectedDepot = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _depotwertText = "CHF 0.00";
        public string DepotwertText
        {
            get => _depotwertText;
            set { _depotwertText = value; OnPropertyChanged(); }
        }

        private string _einstandText = "CHF 0.00";
        public string EinstandText
        {
            get => _einstandText;
            set { _einstandText = value; OnPropertyChanged(); }
        }

        private string _gewinnVerlustText = "CHF 0.00";
        public string GewinnVerlustText
        {
            get => _gewinnVerlustText;
            set { _gewinnVerlustText = value; OnPropertyChanged(); }
        }

        private string _statusText = "Bereit.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ICommand NeuesDepotCommand { get; }
        public ICommand DepotBearbeitenCommand { get; }
        public ICommand DepotLoeschenCommand { get; }

        public VermoegenViewModel()
        {
            NeuesDepotCommand = new RelayCommand(_ => NeuesDepot());
            DepotBearbeitenCommand = new RelayCommand(_ => DepotBearbeiten(), _ => SelectedDepot != null);
            DepotLoeschenCommand = new RelayCommand(_ => DepotLoeschen(), _ => SelectedDepot != null);

            Load();
        }

        private void Load()
        {
            Depots.Clear();
            Positionen.Clear();

            _db.EnsureVermoegenSchema();

            foreach (var d in _db.VermoegenDepotsGetAll().Where(d => d.IstAktiv))
                Depots.Add(d);

            SelectedDepot = Depots.FirstOrDefault();

            var positionen = _db.VermoegenPositionenGetAll()
                .Where(p => p.IstAktiv)
                .ToList();

            foreach (var p in positionen)
            {
                Positionen.Add(new VermoegenPositionRow
                {
                    Depot = p.DepotName,
                    Titel = p.Titel,
                    ISIN = p.ISIN,
                    Anzahl = p.Anzahl,
                    EinstandText = FormatCurrency(p.EinstandWert),
                    AktuellText = p.Marktwert.HasValue ? FormatCurrency(p.Marktwert.Value) : "-",
                    GewinnText = p.GewinnVerlust.HasValue ? FormatCurrency(p.GewinnVerlust.Value) : "-"
                });
            }

            var einstand = positionen.Sum(p => p.EinstandWert);
            var depotwert = positionen.Where(p => p.Marktwert.HasValue).Sum(p => p.Marktwert!.Value);
            var gewinn = depotwert - einstand;

            EinstandText = FormatCurrency(einstand);
            DepotwertText = FormatCurrency(depotwert);
            GewinnVerlustText = FormatCurrency(gewinn);

            StatusText = Positionen.Count == 0
                ? "Noch keine Vermögenspositionen vorhanden."
                : $"{Positionen.Count} Position(en) geladen.";
        }

        private void NeuesDepot()
        {
            var dlg = new VermoegenDepotDialog();
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.VermoegenDepotInsert(dlg.Model);
                Load();
                StatusText = "Depot gespeichert.";
            }
        }

        private void DepotBearbeiten()
        {
            if (SelectedDepot == null)
                return;

            var dlg = new VermoegenDepotDialog(SelectedDepot);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.VermoegenDepotUpdate(dlg.Model);
                Load();
                StatusText = "Depot aktualisiert.";
            }
        }

        private void DepotLoeschen()
        {
            if (SelectedDepot == null)
                return;

            var res = MessageBox.Show(
                $"Depot wirklich ausblenden?\n\n{SelectedDepot.Name}",
                "Depot ausblenden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            _db.VermoegenDepotDelete(SelectedDepot.Id);
            Load();
            StatusText = "Depot ausgeblendet.";
        }

        private static void TrySetOwner(Window dlg)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch
            {
                // keine UI-Blockade
            }
        }

        private static string FormatCurrency(decimal value)
        {
            return string.Format(CultureInfo.GetCultureInfo("de-CH"), "CHF {0:N2}", value);
        }
    }

    public class VermoegenPositionRow
    {
        public string Depot { get; set; } = "";
        public string Titel { get; set; } = "";
        public string ISIN { get; set; } = "";
        public decimal Anzahl { get; set; }

        public string EinstandText { get; set; } = "";
        public string AktuellText { get; set; } = "";
        public string GewinnText { get; set; } = "";
    }
}