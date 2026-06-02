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

        private VermoegenPositionRow? _selectedPosition;
        public VermoegenPositionRow? SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                _selectedPosition = value;
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

        public ICommand NeuePositionCommand { get; }
        public ICommand PositionBearbeitenCommand { get; }
        public ICommand PositionLoeschenCommand { get; }

        public VermoegenViewModel()
        {
            NeuesDepotCommand = new RelayCommand(_ => NeuesDepot());
            DepotBearbeitenCommand = new RelayCommand(_ => DepotBearbeiten(), _ => SelectedDepot != null);
            DepotLoeschenCommand = new RelayCommand(_ => DepotLoeschen(), _ => SelectedDepot != null);

            NeuePositionCommand = new RelayCommand(_ => NeuePosition(), _ => Depots.Any());
            PositionBearbeitenCommand = new RelayCommand(_ => PositionBearbeiten(), _ => SelectedPosition != null);
            PositionLoeschenCommand = new RelayCommand(_ => PositionLoeschen(), _ => SelectedPosition != null);

            Load();
        }

        private void Load()
        {
            var selectedDepotId = SelectedDepot?.Id;
            var selectedPositionId = SelectedPosition?.Id;

            Depots.Clear();
            Positionen.Clear();

            _db.EnsureVermoegenSchema();

            foreach (var d in _db.VermoegenDepotsGetAll().Where(d => d.IstAktiv))
                Depots.Add(d);

            SelectedDepot = selectedDepotId.HasValue
                ? Depots.FirstOrDefault(d => d.Id == selectedDepotId.Value) ?? Depots.FirstOrDefault()
                : Depots.FirstOrDefault();

            var positionen = _db.VermoegenPositionenGetAll()
                .Where(p => p.IstAktiv)
                .ToList();

            foreach (var p in positionen)
                Positionen.Add(ToRow(p));

            SelectedPosition = selectedPositionId.HasValue
                ? Positionen.FirstOrDefault(p => p.Id == selectedPositionId.Value)
                : Positionen.FirstOrDefault();

            var einstand = positionen.Sum(p => p.EinstandWert);
            var depotwert = positionen.Where(p => p.Marktwert.HasValue).Sum(p => p.Marktwert!.Value);
            var gewinn = depotwert - einstand;

            EinstandText = FormatCurrency(einstand);
            DepotwertText = FormatCurrency(depotwert);
            GewinnVerlustText = FormatCurrency(gewinn);

            StatusText = Positionen.Count == 0
                ? "Noch keine Vermögenspositionen vorhanden."
                : $"{Positionen.Count} Position(en) geladen.";

            CommandManager.InvalidateRequerySuggested();
        }

        private VermoegenPositionRow ToRow(VermoegenPosition p)
        {
            return new VermoegenPositionRow
            {
                Id = p.Id,
                DepotId = p.DepotId,
                Depot = p.DepotName,
                Titel = p.Titel,
                ISIN = p.ISIN,
                Anlageklasse = p.Anlageklasse,
                Anzahl = p.Anzahl,
                AnzahlText = FormatNumber(p.Anzahl),
                EinstandText = FormatCurrency(p.EinstandWert),
                AktuellText = p.Marktwert.HasValue ? FormatCurrency(p.Marktwert.Value) : "-",
                GewinnText = p.GewinnVerlust.HasValue ? FormatCurrency(p.GewinnVerlust.Value) : "-"
            };
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

        private void NeuePosition()
        {
            if (!Depots.Any())
            {
                MessageBox.Show(
                    "Bitte zuerst ein Depot erfassen.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new VermoegenPositionDialog(Depots);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.VermoegenPositionInsert(dlg.Model);
                Load();
                StatusText = "Position gespeichert.";
            }
        }

        private void PositionBearbeiten()
        {
            if (SelectedPosition == null)
                return;

            var model = _db.VermoegenPositionenGetAll()
                .FirstOrDefault(p => p.Id == SelectedPosition.Id);

            if (model == null)
            {
                MessageBox.Show(
                    "Die ausgewählte Position wurde nicht gefunden.",
                    "Vermögensposition",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Load();
                return;
            }

            var dlg = new VermoegenPositionDialog(Depots, model);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.VermoegenPositionUpdate(dlg.Model);
                Load();
                StatusText = "Position aktualisiert.";
            }
        }

        private void PositionLoeschen()
        {
            if (SelectedPosition == null)
                return;

            var res = MessageBox.Show(
                $"Position wirklich ausblenden?\n\n{SelectedPosition.Titel}",
                "Position ausblenden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            _db.VermoegenPositionDelete(SelectedPosition.Id);
            Load();
            StatusText = "Position ausgeblendet.";
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

        private static string FormatNumber(decimal value)
        {
            return value.ToString("N8", CultureInfo.GetCultureInfo("de-CH")).TrimEnd('0').TrimEnd('.');
        }
    }

    public class VermoegenPositionRow
    {
        public int Id { get; set; }
        public int DepotId { get; set; }

        public string Depot { get; set; } = "";
        public string Titel { get; set; } = "";
        public string ISIN { get; set; } = "";
        public string Anlageklasse { get; set; } = "";

        public decimal Anzahl { get; set; }
        public string AnzahlText { get; set; } = "";

        public string EinstandText { get; set; } = "";
        public string AktuellText { get; set; } = "";
        public string GewinnText { get; set; } = "";
    }
}