using DocumentFormat.OpenXml.Wordprocessing;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Helpers;
using Microsoft.Win32;
using MyCoinFlow.Importing;
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
        private readonly Dictionary<string, decimal?> _fxNachChfCache = new();

        public ObservableCollection<VermoegenDepot> Depots { get; } = new();
        public ObservableCollection<VermoegenDepot> DepotFilterListe { get; } = new();
        public ObservableCollection<string> AnlageklasseFilterListe { get; } = new();
        public ObservableCollection<VermoegenPositionRow> Positionen { get; } = new();

        public ObservableCollection<string> ZeitraumFilterListe { get; } = new()
        {
            "1 Monat",
            "3 Monate",
            "6 Monate",
            "1 Jahr",
            "Alles"
        };

        private string _selectedZeitraumFilter = "3 Monate";
        public string SelectedZeitraumFilter
        {
            get => _selectedZeitraumFilter;
            set
            {
                _selectedZeitraumFilter = string.IsNullOrWhiteSpace(value) ? "Alles" : value;
                OnPropertyChanged();
                UpdateDepotVerlauf();
            }
        }

        private ISeries[] _depotVerlaufSeries = Array.Empty<ISeries>();

        private ISeries[] _vermoegenAufteilungSeries = Array.Empty<ISeries>();
        public ISeries[] VermoegenAufteilungSeries
        {
            get => _vermoegenAufteilungSeries;
            set { _vermoegenAufteilungSeries = value; OnPropertyChanged(); }
        }
        public ISeries[] DepotVerlaufSeries
        {
            get => _depotVerlaufSeries;
            set { _depotVerlaufSeries = value; OnPropertyChanged(); }
        }

        private Axis[] _depotVerlaufXAxes = Array.Empty<Axis>();
        public Axis[] DepotVerlaufXAxes
        {
            get => _depotVerlaufXAxes;
            set { _depotVerlaufXAxes = value; OnPropertyChanged(); }
        }

        private Axis[] _depotVerlaufYAxes = Array.Empty<Axis>();
        public Axis[] DepotVerlaufYAxes
        {
            get => _depotVerlaufYAxes;
            set { _depotVerlaufYAxes = value; OnPropertyChanged(); }
        }

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
        public ICommand PositionenImportierenCommand { get; }
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
            PositionenImportierenCommand = new RelayCommand(_ => PositionenImportieren(), _ => SelectedDepotFilter != null && SelectedDepotFilter.Id > 0);
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
            _fxNachChfCache.Clear();

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

            if (selectedFilterId.HasValue)
            {
                SelectedDepotFilter =
                    DepotFilterListe.FirstOrDefault(d => d.Id == selectedFilterId.Value)
                    ?? DepotFilterListe.FirstOrDefault();
            }
            else
            {
                SelectedDepotFilter =
                    DepotFilterListe.FirstOrDefault(d => d.Id > 0 && d.IstStandard && d.IstAktiv)
                    ?? DepotFilterListe.FirstOrDefault();
            }

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
            AddAnlageklasseIfMissing("Fonds");
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
            UpdateDepotVerlauf();
            UpdateVermoegenAufteilung(list);

            StatusText = Positionen.Count == 0
                ? "Keine passenden Vermögenspositionen vorhanden."
                : $"{Positionen.Count} Position(en) angezeigt.";

            SelectedPosition = Positionen.FirstOrDefault();

            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateSummary(List<VermoegenPosition> list)
        {
            decimal einstandChf = 0m;
            decimal depotwertChf = 0m;
            int fehlendeFx = 0;

            foreach (var p in list)
            {
                var fxMarkt = GetFxKursNachChf(p.Waehrung);
                var fxEinstand = GetFxKursNachChf(p.EffektiveEinstandWaehrung);

                if (!fxMarkt.HasValue || !fxEinstand.HasValue)
                {
                    fehlendeFx++;
                    continue;
                }

                einstandChf += p.EinstandWert * fxEinstand.Value;

                if (p.Marktwert.HasValue)
                    depotwertChf += p.Marktwert.Value * fxMarkt.Value;
            }

            var gewinnChf = depotwertChf - einstandChf;

            EinstandText = FormatCurrency(einstandChf, "CHF");
            DepotwertText = FormatCurrency(depotwertChf, "CHF");

            if (einstandChf > 0)
            {
                var performance = gewinnChf / einstandChf * 100m;
                GewinnVerlustText = $"{FormatCurrency(gewinnChf, "CHF")} ({FormatPercent(performance)})";
            }
            else
            {
                GewinnVerlustText = FormatCurrency(gewinnChf, "CHF");
            }

            if (fehlendeFx > 0)
                StatusText = $"{fehlendeFx} Position(en) ohne FX-Kurs.";
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

        private void UpdateDepotVerlauf()
        {
            var depotId = SelectedDepotFilter != null && SelectedDepotFilter.Id > 0
                ? SelectedDepotFilter.Id
                : (int?)null;

            var startDatum = ZeitraumStartdatum(SelectedZeitraumFilter);

            var daten = _db.VermoegenDepotVerlaufGet(depotId)
                .Where(x => !startDatum.HasValue || x.Datum.Date >= startDatum.Value)
                .OrderBy(x => x.Datum)
                .ToList();

            DepotVerlaufSeries = new ISeries[]
            {
        new LineSeries<decimal>
        {
            Values = daten.Select(x => x.DepotwertChf).ToArray(),
            Name = "Depotwert CHF",
            // Bei vielen Datenpunkten keine Einzelpunkte zeichnen, sonst verklumpt die Linie.
            GeometrySize = daten.Count > 60 ? 0 : 6,
            Fill = null
        }
            };

            DepotVerlaufXAxes = new Axis[]
            {
        new Axis
        {
            Labels = daten.Select(x => x.Datum.ToString("dd.MM.yy")).ToArray(),
            LabelsRotation = 35
        }
            };

            DepotVerlaufYAxes = new Axis[]
            {
        new Axis
        {
            Labeler = value => value.ToString("N0", CultureInfo.GetCultureInfo("de-CH"))
        }
            };
        }

        private void UpdateVermoegenAufteilung(List<VermoegenPosition> list)
        {
            var daten = list
                .Select(p =>
                {
                    var fx = GetFxKursNachChf(p.Waehrung);

                    if (!p.Marktwert.HasValue || !fx.HasValue)
                        return null;

                    return new
                    {
                        Anlageklasse = string.IsNullOrWhiteSpace(p.Anlageklasse)
                            ? "Sonstiges"
                            : p.Anlageklasse.Trim(),
                        MarktwertChf = p.Marktwert.Value * fx.Value
                    };
                })
                .Where(x => x != null && x.MarktwertChf > 0)
                .GroupBy(x => x!.Anlageklasse)
                .Select(g => new
                {
                    Anlageklasse = g.Key,
                    Wert = g.Sum(x => x!.MarktwertChf)
                })
                .OrderByDescending(x => x.Wert)
                .ToList();

            var total = daten.Sum(x => x.Wert);

            VermoegenAufteilungSeries = daten
                .Select(x =>
                {
                    var percent = total > 0
                        ? x.Wert / total * 100m
                        : 0m;

                    return (ISeries)new PieSeries<decimal>
                    {
                        Values = new[] { x.Wert },
                        Name = $"{x.Anlageklasse} · {percent:N1}% · {FormatCurrency(x.Wert, "CHF")}",
                        InnerRadius = 55
                    };
                })
                .ToArray();
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
            try
            {
                StatusText = "Kurse werden aktualisiert...";

                var service = new VermoegenKursUpdateService();
                var result = await service.AktualisierenAsync();

                Load();

                StatusText = string.IsNullOrWhiteSpace(result.Meldung)
                    ? "Kursaktualisierung abgeschlossen."
                    : result.Meldung;

                if (result.PositionenAktualisiert == 0 &&
                    result.PositionenOhneKursbasis == 0 &&
                    result.FxGespeichert == 0 &&
                    result.FxOhneErgebnis == 0 &&
                    result.KurseNachgeholt == 0 &&
                    result.FxNachgeholt == 0)
                {
                    MessageBox.Show(
                        StatusText,
                        "Kurse aktualisieren",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusText = "Kursaktualisierung fehlgeschlagen.";

                MessageBox.Show(
                    "Kursaktualisierung fehlgeschlagen:\n" + ex.Message,
                    "Kurse aktualisieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private VermoegenPositionRow ToRow(VermoegenPosition p)
        {
            var fx = GetFxKursNachChf(p.Waehrung);
            var fxEinstand = GetFxKursNachChf(p.EffektiveEinstandWaehrung);

            decimal? marktwertChf = p.Marktwert.HasValue && fx.HasValue
                ? p.Marktwert.Value * fx.Value
                : null;

            decimal? einstandChf = fxEinstand.HasValue
                ? p.EinstandWert * fxEinstand.Value
                : null;

            // Gewinn immer als Differenz der CHF-Werte, damit auch der Fall
            // "Einstand in CHF, Handel in USD" korrekt abgebildet ist.
            decimal? gewinnChf = marktwertChf.HasValue && einstandChf.HasValue
                ? marktwertChf.Value - einstandChf.Value
                : null;

            return new VermoegenPositionRow
            {
                Id = p.Id,
                DepotId = p.DepotId,
                Depot = p.DepotName,
                Titel = p.Titel,
                ISIN = p.ISIN,
                Anlageklasse = p.Anlageklasse,
                Waehrung = p.Waehrung,

                Anzahl = p.Anzahl,
                AnzahlText = FormatNumber(p.Anzahl),

                EinstandText = FormatCurrency(p.EinstandWert, p.EffektiveEinstandWaehrung),

                AktuellText = p.Marktwert.HasValue
                    ? FormatCurrency(p.Marktwert.Value, p.Waehrung)
                    : "-",

                GewinnText = BuildGewinnText(p, gewinnChf, einstandChf),

                FxKursText = fx.HasValue
                    ? FormatFx(fx.Value)
                    : "-",

                MarktwertChfText = marktwertChf.HasValue
                    ? FormatCurrency(marktwertChf.Value, "CHF")
                    : "-",

                GewinnChfText = gewinnChf.HasValue
                    ? FormatCurrency(gewinnChf.Value, "CHF")
                    : "-",

                KursdatumText = p.KursDatum.HasValue
                    ? p.KursDatum.Value.ToString("dd.MM.yyyy")
                    : "-"
            };
        }

        private string BuildGewinnText(VermoegenPosition p, decimal? gewinnChf, decimal? einstandChf)
        {
            // Standardfall: Einstand und Handel in derselben Währung.
            if (p.GewinnVerlust.HasValue)
            {
                if (p.EinstandWert > 0)
                {
                    var performance = p.GewinnVerlust.Value / p.EinstandWert * 100m;
                    return $"{FormatCurrency(p.GewinnVerlust.Value, p.Waehrung)} ({FormatPercent(performance)})";
                }

                return FormatCurrency(p.GewinnVerlust.Value, p.Waehrung);
            }

            // Abweichende Einstandswährung: Gewinn nur in CHF vergleichbar.
            if (gewinnChf.HasValue)
            {
                if (einstandChf.HasValue && einstandChf.Value > 0)
                {
                    var performance = gewinnChf.Value / einstandChf.Value * 100m;
                    return $"{FormatCurrency(gewinnChf.Value, "CHF")} ({FormatPercent(performance)})";
                }

                return FormatCurrency(gewinnChf.Value, "CHF");
            }

            return "-";
        }

        private decimal? GetFxKursNachChf(string waehrung)
        {
            var w = string.IsNullOrWhiteSpace(waehrung)
                ? "CHF"
                : waehrung.Trim().ToUpperInvariant();

            if (_fxNachChfCache.TryGetValue(w, out var cached))
                return cached;

            var fx = _db.VermoegenFxKursNachChfGetLatest(w);
            _fxNachChfCache[w] = fx;
            return fx;
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

        private void PositionenImportieren()
        {
            if (SelectedDepotFilter == null || SelectedDepotFilter.Id <= 0)
            {
                MessageBox.Show(
                    "Bitte zuerst ein konkretes Depot auswählen. Der Import kann nicht in 'Alle Depots' erfolgen.",
                    "Positionen importieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Depotpositionen aus Excel importieren",
                Filter = "Excel-Dateien (*.xlsx;*.xls)|*.xlsx;*.xls|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                var importer = new VermoegenPositionExcelImporter();
                var result = importer.Import(dlg.FileName, SelectedDepotFilter.Id);

                Load();

                MessageBox.Show(
                    $"Import abgeschlossen.\n\n" +
                    $"- Gelesene Zeilen: {result.RowsRead}\n" +
                    $"- Importiert: {result.RowsImported}\n" +
                    $"- Übersprungen: {result.RowsSkipped}\n" +
                    $"- Fehler: {result.RowsWithErrors}",
                    "Positionen importieren",
                    MessageBoxButton.OK,
                    result.RowsWithErrors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                StatusText =
                    $"Import abgeschlossen: {result.RowsImported} importiert, " +
                    $"{result.RowsSkipped} übersprungen, {result.RowsWithErrors} Fehler.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Import fehlgeschlagen:\n" + ex.Message,
                    "Positionen importieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Import fehlgeschlagen.";
            }
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

        private static DateTime? ZeitraumStartdatum(string auswahl)
        {
            return auswahl switch
            {
                "1 Monat" => DateTime.Today.AddMonths(-1),
                "3 Monate" => DateTime.Today.AddMonths(-3),
                "6 Monate" => DateTime.Today.AddMonths(-6),
                "1 Jahr" => DateTime.Today.AddYears(-1),
                _ => null // "Alles"
            };
        }

        private static string FormatCurrency(decimal value, string waehrung = "CHF")
        {
            var w = string.IsNullOrWhiteSpace(waehrung)
                ? "CHF"
                : waehrung.Trim().ToUpperInvariant();

            return string.Format(CultureInfo.GetCultureInfo("de-CH"), "{0} {1:N2}", w, value);
        }

        private static string FormatFx(decimal value)
        {
            return value.ToString("N6", CultureInfo.GetCultureInfo("de-CH"));
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
        public string Waehrung { get; set; } = "CHF";

        public decimal Anzahl { get; set; }
        public string AnzahlText { get; set; } = "";

        public string EinstandText { get; set; } = "";
        public string AktuellText { get; set; } = "";
        public string GewinnText { get; set; } = "";

        public string FxKursText { get; set; } = "";
        public string MarktwertChfText { get; set; } = "";
        public string GewinnChfText { get; set; } = "";

        public string KursdatumText { get; set; } = "";
    }
}