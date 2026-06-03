using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class VermoegenViewModel : BaseViewModel
    {
        private const string AlleAnlageklassenText = "Alle Anlageklassen";

        private readonly DatabaseService _db = new();
        private readonly KursService _kursService = new();
        private readonly List<VermoegenPosition> _allePositionen = new();

        public ObservableCollection<VermoegenDepot> Depots { get; } = new();
        public ObservableCollection<VermoegenDepot> DepotFilterListe { get; } = new();
        public ObservableCollection<string> AnlageklasseFilterListe { get; } = new();
        public ObservableCollection<VermoegenPositionRow> Positionen { get; } = new();

        private string _suchtext = "";
        public string Suchtext
        {
            get => _suchtext;
            set { _suchtext = value ?? ""; OnPropertyChanged(); }
        }

        private VermoegenDepot? _selectedDepotFilter;
        public VermoegenDepot? SelectedDepotFilter
        {
            get => _selectedDepotFilter;
            set
            {
                _selectedDepotFilter = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _selectedAnlageklasseFilter = AlleAnlageklassenText;
        public string SelectedAnlageklasseFilter
        {
            get => _selectedAnlageklasseFilter;
            set
            {
                _selectedAnlageklasseFilter = value ?? AlleAnlageklassenText;
                OnPropertyChanged();
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

        private string _filterTitelText = "Alle Depots";
        public string FilterTitelText
        {
            get => _filterTitelText;
            set { _filterTitelText = value; OnPropertyChanged(); }
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

        public ICommand SucheCommand { get; }
        public ICommand FilterLeerenCommand { get; }

        public ICommand NeuesDepotCommand { get; }
        public ICommand DepotBearbeitenCommand { get; }
        public ICommand DepotLoeschenCommand { get; }

        public ICommand NeuePositionCommand { get; }
        public ICommand PositionBearbeitenCommand { get; }
        public ICommand PositionLoeschenCommand { get; }

        public ICommand KurseAktualisierenCommand { get; }
        public ICommand ApiEinstellungCommand { get; }
        public ICommand KursHistorieCommand { get; }

        public VermoegenViewModel()
        {
            SucheCommand = new RelayCommand(_ => ApplyFilter());
            FilterLeerenCommand = new RelayCommand(_ => FilterLeeren());

            NeuesDepotCommand = new RelayCommand(_ => NeuesDepot());
            DepotBearbeitenCommand = new RelayCommand(_ => DepotBearbeiten(), _ => SelectedDepotFilter != null && SelectedDepotFilter.Id > 0);
            DepotLoeschenCommand = new RelayCommand(_ => DepotLoeschen(), _ => SelectedDepotFilter != null && SelectedDepotFilter.Id > 0);

            NeuePositionCommand = new RelayCommand(_ => NeuePosition(), _ => Depots.Any());
            PositionBearbeitenCommand = new RelayCommand(_ => PositionBearbeiten(), _ => SelectedPosition != null);
            PositionLoeschenCommand = new RelayCommand(_ => PositionLoeschen(), _ => SelectedPosition != null);
            ApiEinstellungCommand = new RelayCommand(_ => ApiEinstellung());

            KurseAktualisierenCommand = new RelayCommand(_ => KurseAktualisieren(), _ => _allePositionen.Any(p => p.IstAktiv));
            KursHistorieCommand = new RelayCommand(p => KursHistorieAnzeigen(p), p => p is VermoegenPositionRow);

            Load();
        }

        private void Load()
        {
            var selectedFilterId = SelectedDepotFilter?.Id;
            var selectedAnlageklasse = SelectedAnlageklasseFilter;
            var selectedPositionId = SelectedPosition?.Id;

            Depots.Clear();
            DepotFilterListe.Clear();
            AnlageklasseFilterListe.Clear();
            Positionen.Clear();
            _allePositionen.Clear();

            _db.EnsureVermoegenSchema();

            foreach (var d in _db.VermoegenDepotsGetAll().Where(d => d.IstAktiv))
                Depots.Add(d);

            DepotFilterListe.Add(new VermoegenDepot
            {
                Id = 0,
                Name = "Alle Depots",
                Waehrung = "CHF",
                IstAktiv = true
            });

            foreach (var d in Depots)
                DepotFilterListe.Add(d);

            foreach (var p in _db.VermoegenPositionenGetAll().Where(p => p.IstAktiv))
                _allePositionen.Add(p);

            BuildAnlageklasseFilterListe();

            SelectedDepotFilter = selectedFilterId.HasValue
                ? DepotFilterListe.FirstOrDefault(d => d.Id == selectedFilterId.Value) ?? DepotFilterListe.FirstOrDefault()
                : DepotFilterListe.FirstOrDefault();

            SelectedAnlageklasseFilter =
                !string.IsNullOrWhiteSpace(selectedAnlageklasse) && AnlageklasseFilterListe.Contains(selectedAnlageklasse)
                    ? selectedAnlageklasse
                    : AlleAnlageklassenText;

            ApplyFilter();

            SelectedPosition = selectedPositionId.HasValue
                ? Positionen.FirstOrDefault(p => p.Id == selectedPositionId.Value)
                : Positionen.FirstOrDefault();

            CommandManager.InvalidateRequerySuggested();
        }

        private void BuildAnlageklasseFilterListe()
        {
            AnlageklasseFilterListe.Add(AlleAnlageklassenText);

            foreach (var klasse in _allePositionen
                         .Select(p => p.Anlageklasse)
                         .Where(k => !string.IsNullOrWhiteSpace(k))
                         .Distinct()
                         .OrderBy(k => k))
            {
                AnlageklasseFilterListe.Add(klasse);
            }

            AddAnlageklasseIfMissing("Aktie");
            AddAnlageklasseIfMissing("ETF");
            AddAnlageklasseIfMissing("Obligation");
            AddAnlageklasseIfMissing("Kryptowährung");
            AddAnlageklasseIfMissing("Edelmetall");
            AddAnlageklasseIfMissing("Immobilie");
            AddAnlageklasseIfMissing("Sonstiges");
        }

        private void AddAnlageklasseIfMissing(string value)
        {
            if (!AnlageklasseFilterListe.Contains(value))
                AnlageklasseFilterListe.Add(value);
        }

        private void ApplyFilter()
        {
            Positionen.Clear();

            var filtered = _allePositionen.AsEnumerable();

            if (SelectedDepotFilter != null && SelectedDepotFilter.Id > 0)
                filtered = filtered.Where(p => p.DepotId == SelectedDepotFilter.Id);

            if (!string.IsNullOrWhiteSpace(SelectedAnlageklasseFilter) &&
                SelectedAnlageklasseFilter != AlleAnlageklassenText)
            {
                filtered = filtered.Where(p =>
                    string.Equals(p.Anlageklasse, SelectedAnlageklasseFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(Suchtext))
            {
                var needle = Suchtext.Trim();

                filtered = filtered.Where(p =>
                    ContainsIgnoreCase(p.Titel, needle) ||
                    ContainsIgnoreCase(p.ISIN, needle) ||
                    ContainsIgnoreCase(p.DepotName, needle));
            }

            var list = filtered.ToList();

            foreach (var p in list)
                Positionen.Add(ToRow(p));

            UpdateSummary(list);
            UpdateFilterTitel();

            StatusText = Positionen.Count == 0
                ? "Keine passenden Vermögenspositionen vorhanden."
                : $"{Positionen.Count} Position(en) angezeigt.";

            SelectedPosition = Positionen.FirstOrDefault();

            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateSummary(List<VermoegenPosition> list)
        {
            var einstand = list.Sum(p => p.EinstandWert);
            var depotwert = list.Where(p => p.Marktwert.HasValue).Sum(p => p.Marktwert!.Value);
            var gewinn = depotwert - einstand;

            EinstandText = FormatCurrency(einstand);
            DepotwertText = FormatCurrency(depotwert);

            if (einstand > 0)
            {
                var performance = gewinn / einstand * 100m;
                GewinnVerlustText = $"{FormatCurrency(gewinn)} ({FormatPercent(performance)})";
            }
            else
            {
                GewinnVerlustText = FormatCurrency(gewinn);
            }
        }

        private void UpdateFilterTitel()
        {
            var depotText = SelectedDepotFilter == null || SelectedDepotFilter.Id <= 0
                ? "Alle Depots"
                : SelectedDepotFilter.Name;

            var klasseText = string.IsNullOrWhiteSpace(SelectedAnlageklasseFilter) ||
                             SelectedAnlageklasseFilter == AlleAnlageklassenText
                ? ""
                : $" · {SelectedAnlageklasseFilter}";

            FilterTitelText = depotText + klasseText;
        }

        private void FilterLeeren()
        {
            Suchtext = "";
            SelectedDepotFilter = DepotFilterListe.FirstOrDefault();
            SelectedAnlageklasseFilter = AlleAnlageklassenText;
            ApplyFilter();
        }

        private async void KurseAktualisieren()
        {
            var einstellung = _db.VermoegenApiEinstellungGet();

            if (!einstellung.Aktiv || string.IsNullOrWhiteSpace(einstellung.ApiKey))
            {
                MessageBox.Show(
                    "Bitte zuerst den EODHD API-Key erfassen.",
                    "Kurse aktualisieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText = "Kein aktiver API-Key vorhanden.";
                return;
            }

            var positionen = _allePositionen
                .Where(p => p.IstAktiv)
                .Where(p => !string.IsNullOrWhiteSpace(p.Symbol))
                .ToList();

            if (positionen.Count == 0)
            {
                MessageBox.Show(
                    "Es sind keine aktiven Positionen mit Symbol vorhanden.",
                    "Kurse aktualisieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText = "Keine Positionen mit Symbol vorhanden.";
                return;
            }

            int ok = 0;
            int fehler = 0;

            StatusText = "Kurse werden aktualisiert...";

            foreach (var p in positionen)
            {
                var result = await _kursService.HoleAktuellenKursAsync(
                    p.Symbol,
                    p.Boerse,
                    einstellung.ApiKey);

                if (result == null)
                {
                    fehler++;
                    continue;
                }

                _db.VermoegenPositionKursUpdate(
    p.Id,
    result.Kurs,
    result.KursDatum);

                _db.VermoegenKursHistorieInsertIfMissing(
                    p.Id,
                    result.KursDatum,
                    result.Kurs,
                    "EODHD");

                ok++;
            }

            Load();

            StatusText = $"Kursaktualisierung abgeschlossen: {ok} aktualisiert, {fehler} ohne Ergebnis.";
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
                GewinnText = BuildGewinnText(p),
                KursdatumText = p.KursDatum.HasValue ? p.KursDatum.Value.ToString("dd.MM.yyyy") : "-"
            };
        }

        private string BuildGewinnText(VermoegenPosition p)
        {
            if (!p.GewinnVerlust.HasValue)
                return "-";

            if (p.EinstandWert > 0)
            {
                var performance = p.GewinnVerlust.Value / p.EinstandWert * 100m;
                return $"{FormatCurrency(p.GewinnVerlust.Value)} ({FormatPercent(performance)})";
            }

            return FormatCurrency(p.GewinnVerlust.Value);
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
            if (SelectedDepotFilter == null || SelectedDepotFilter.Id <= 0)
                return;

            var depot = Depots.FirstOrDefault(d => d.Id == SelectedDepotFilter.Id);
            if (depot == null)
                return;

            var dlg = new VermoegenDepotDialog(depot);
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
            if (SelectedDepotFilter == null || SelectedDepotFilter.Id <= 0)
                return;

            var depot = Depots.FirstOrDefault(d => d.Id == SelectedDepotFilter.Id);
            if (depot == null)
                return;

            var res = MessageBox.Show(
                $"Depot wirklich ausblenden?\n\n{depot.Name}",
                "Depot ausblenden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            _db.VermoegenDepotDelete(depot.Id);
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

        private void ApiEinstellung()
        {
            var model = _db.VermoegenApiEinstellungGet();

            var dlg = new VermoegenApiEinstellungDialog(model);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                _db.VermoegenApiEinstellungSave(dlg.Model);
                StatusText = "API-Einstellungen gespeichert.";
            }
        }

        private void KursHistorieAnzeigen(object? parameter)
        {
            if (parameter is not VermoegenPositionRow row)
                return;

            var model = _db.VermoegenPositionenGetAll()
                .FirstOrDefault(p => p.Id == row.Id);

            if (model == null)
            {
                MessageBox.Show(
                    "Die ausgewählte Position wurde nicht gefunden.",
                    "Kursverlauf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Load();
                return;
            }

            var dlg = new VermoegenKursHistorieWindow(model);
            TrySetOwner(dlg);
            dlg.ShowDialog();
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

        private static bool ContainsIgnoreCase(string? source, string value)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
                return false;

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatCurrency(decimal value)
        {
            return string.Format(CultureInfo.GetCultureInfo("de-CH"), "CHF {0:N2}", value);
        }

        private static string FormatPercent(decimal value)
        {
            return string.Format(CultureInfo.GetCultureInfo("de-CH"), "{0:+0.00;-0.00;0.00}%", value);
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
        public string KursdatumText { get; set; } = "";
    }
}