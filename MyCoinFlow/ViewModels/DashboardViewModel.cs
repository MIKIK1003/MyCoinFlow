using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Dashboard: liefert Charts, Filter (Nummernkreise) und Kennzahlen.
    /// Datenquelle: DatabaseService.
    /// </summary>
    public class DashboardViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Header =====

        public ObservableCollection<GroupingOption> GroupingOptions { get; } =
            new ObservableCollection<GroupingOption>
            {
                new GroupingOption("Art", "Art"),
                new GroupingOption("Gruppe", "Gruppe"),
                new GroupingOption("Untergruppe", "Untergruppe")
            };

        private GroupingOption? _selectedGrouping;
        public GroupingOption? SelectedGrouping
        {
            get => _selectedGrouping;
            set
            {
                if (_selectedGrouping == value) return;
                _selectedGrouping = value;
                OnPropertyChanged();
                LoadCharts(); // wichtig: Wechsel muss Charts neu bauen
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SelectAllRangesCommand { get; }
        public ICommand SelectNoneRangesCommand { get; }
        public ICommand ApplyFiltersCommand { get; }

        // ===== Sidebar =====

        private bool _showPercent = true; // Default: EIN
        public bool ShowPercent
        {
            get => _showPercent;
            set
            {
                if (_showPercent == value) return;
                _showPercent = value;
                OnPropertyChanged();
                LoadCharts(); // wichtig: Pie-Legende/Order neu bauen
            }
        }

        public ObservableCollection<NumberRangeVm> NumberRanges { get; } = new();

        private int _openImportCount;
        public int OpenImportCount
        {
            get => _openImportCount;
            private set { _openImportCount = value; OnPropertyChanged(); }
        }

        private int _bankImportItemCount;
        public int BankImportItemCount
        {
            get => _bankImportItemCount;
            private set { _bankImportItemCount = value; OnPropertyChanged(); }
        }

        private string _zeitraumLabel = "";
        public string ZeitraumLabel
        {
            get => _zeitraumLabel;
            private set { _zeitraumLabel = value; OnPropertyChanged(); }
        }

        // ===== Charts =====

        private string _columnChartTitle = "Budget vs. IST";
        public string ColumnChartTitle
        {
            get => _columnChartTitle;
            private set { _columnChartTitle = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ISeries> ColumnSeries { get; } = new();
        public ObservableCollection<Axis> XAxes { get; } = new();
        public ObservableCollection<Axis> YAxes { get; } = new();

        public ObservableCollection<ISeries> PieSeries { get; } = new();

        public ObservableCollection<ISeries> TopDevSeries { get; } = new();
        public ObservableCollection<Axis> TopDevXAxes { get; } = new();
        public ObservableCollection<Axis> TopDevYAxes { get; } = new();

        public ObservableCollection<ISeries> BankSeries { get; } = new();
        public ObservableCollection<Axis> BankXAxes { get; } = new();
        public ObservableCollection<Axis> BankYAxes { get; } = new();

        // ===== Konstruktor =====

        public DashboardViewModel()
        {
            // Default: Untergruppe (wie früher gewünscht)
            SelectedGrouping = GroupingOptions.FirstOrDefault(o => o.Key == "Untergruppe")
                               ?? GroupingOptions.First();

            RefreshCommand = new RelayCommand(_ => LoadAll());
            ApplyFiltersCommand = new RelayCommand(_ => LoadAll());

            SelectAllRangesCommand = new RelayCommand(_ =>
            {
                foreach (var r in NumberRanges) r.IsSelected = true;
                LoadAll();
            });

            SelectNoneRangesCommand = new RelayCommand(_ =>
            {
                foreach (var r in NumberRanges) r.IsSelected = false;
                LoadAll();
            });

            LoadNumberRanges(); // Default: nur Einnahmen + Ausgaben
            LoadAll();
        }

        /// <summary>
        /// Lädt die Nummernkreise aus der DB (NumberRangeRules).
        /// Default: Nur "Einnahmen" und "Ausgaben" sind aktiv.
        /// Investitionen/Amortisationen/Durchlaufkonten bleiben default inaktiv,
        /// unabhängig von "Richtung".
        /// </summary>
        void LoadNumberRanges()
        {
            NumberRanges.Clear();

            var rules = _db.LadeNummernRegeln();

            // Kontenplan einmal laden, damit wir Nummernkreise ohne Treffer ausblenden können
            var accounts = _db.LadeKontenplan();

            foreach (var r in rules.OrderBy(x => x.RangeStart))
            {
                // Nur Regeln anzeigen, die im aktuellen Mandanten mindestens 1 Konto treffen
                bool hasHit = accounts.Any(a => a.Kontonummer >= r.RangeStart && a.Kontonummer <= r.RangeEnd);
                if (!hasHit)
                    continue;

                var label = !string.IsNullOrWhiteSpace(r.Bezeichnung)
                    ? r.Bezeichnung!
                    : $"{r.Richtung} {r.RangeStart}-{r.RangeEnd}";

                NumberRanges.Add(new NumberRangeVm(r.RangeStart, r.RangeEnd, label)
                {
                    IsSelected = IsDefaultSelectedKontenkreis(label)
                });
            }

            OnPropertyChanged(nameof(NumberRanges));
        }

        private static bool IsDefaultSelectedKontenkreis(string label)
        {
            var s = (label ?? "").Trim().ToLowerInvariant();

            // Default aktiv: Einnahmen + Ausgaben
            var isEinnahmen = s.Contains("einnahm");
            var isAusgaben = s.Contains("ausgab");

            if (!isEinnahmen && !isAusgaben) return false;

            // Default explizit NICHT:
            if (s.Contains("invest")) return false;
            if (s.Contains("amort")) return false;
            if (s.Contains("durchlauf")) return false;

            return true;
        }

        /// <summary>
        /// Lädt alle Dashboard-Daten (Kennzahlen + Charts).
        /// </summary>
        private void LoadAll()
        {
            LoadCounts();
            LoadCharts();
        }

        /// <summary>
        /// Lädt die Kennzahlen für die Sidebar (offene Imports).
        /// </summary>
        private void LoadCounts()
        {
            OpenImportCount = _db.CountCreditCardStaging();
            BankImportItemCount = _db.CountBankImportItem();

            ZeitraumLabel = "Aktiver Budgetzeitraum (falls vorhanden) – Stand: " +
                            DateTime.Today.ToString("d", CultureInfo.GetCultureInfo("de-CH"));
        }

        /// <summary>
        /// Baut Charts aus Kontenplan (Budgetwert + Gebucht) und Banksalden.
        /// </summary>
        private void LoadCharts()
        {
            var allAccounts = _db.LadeKontenplan();

            // Filter: nur ausgewählte Nummernkreise
            var selectedRanges = NumberRanges.Where(x => x.IsSelected).ToList();
            if (selectedRanges.Count > 0)
            {
                var filtered = allAccounts
                    .Where(a => selectedRanges.Any(r => a.Kontonummer >= r.Start && a.Kontonummer <= r.End))
                    .ToList();

                // Wichtig: Wenn Filter 0 Konten liefert, NICHT alles "verschwinden" lassen.
                // Fallback auf ungefilterte Darstellung (robust für neue Mandanten / falsche Nummernkreise).
                if (filtered.Count > 0)
                    allAccounts = filtered;
            }

            string key = SelectedGrouping?.Key ?? "Untergruppe";

            string GroupKey(KontoplanEintrag a)
            {
                return key switch
                {
                    "Art" => a.Art ?? "",
                    "Gruppe" => a.Gruppe ?? "",
                    _ => a.Untergruppe ?? ""
                };
            }

            var groups = allAccounts
                .GroupBy(GroupKey)
                .Select(g => new
                {
                    Label = string.IsNullOrWhiteSpace(g.Key) ? "(leer)" : g.Key,
                    Budget = g.Sum(x => x.Budgetwert ?? 0m),
                    Ist = g.Sum(x => x.Gebucht)
                })
                .OrderByDescending(x => Math.Abs(x.Ist))
                .ToList();

            // ---- ColumnChart: Budget vs IST ----
            ColumnSeries.Clear();
            XAxes.Clear();
            YAxes.Clear();

            var labels = groups.Select(x => x.Label).ToArray();
            var budgetVals = groups.Select(x => (double)x.Budget).ToArray();
            var istVals = groups.Select(x => (double)x.Ist).ToArray();

            ColumnSeries.Add(new ColumnSeries<double> { Name = "Budget", Values = budgetVals });
            ColumnSeries.Add(new ColumnSeries<double> { Name = "IST", Values = istVals });

            // Verbesserung 1: gedrehte Beschriftung (leicht schräg)
            XAxes.Add(new Axis { Labels = labels, LabelsRotation = 60, TextSize = 14 });
            YAxes.Add(new Axis());

            ColumnChartTitle = $"Budget vs. IST ({SelectedGrouping?.Label ?? "Untergruppe"})";

            // ---- Pie: IST-Verteilung ----
            PieSeries.Clear();

            var pieItems = groups
                .Where(x => x.Ist != 0)
                .Select(x => new { x.Label, Val = Math.Abs((double)x.Ist) })
                .OrderByDescending(x => x.Val) // Verbesserung 2: nach Betrag sortieren (Legende + Segmente)
                .ToList();

            var total = pieItems.Sum(x => x.Val);

            foreach (var p in pieItems)
            {
                var name = p.Label;

                // Verbesserung 3: % Anzeige muss sichtbar wirken
                if (ShowPercent && total > 0)
                {
                    var pct = p.Val / total;
                    name = $"{p.Label} ({pct:P0})";
                }

                PieSeries.Add(new PieSeries<double>
                {
                    Name = name,
                    Values = new[] { p.Val }
                });
            }

            // ---- Top-Abweichungen ----
            TopDevSeries.Clear();
            TopDevXAxes.Clear();
            TopDevYAxes.Clear();

            var top = groups
                .Select(x => new { x.Label, Dev = (double)(x.Ist - x.Budget) })
                .OrderByDescending(x => Math.Abs(x.Dev))
                .Take(8)
                .ToList();

            TopDevSeries.Add(new RowSeries<double>
            {
                Name = "Abweichung",
                Values = top.Select(x => x.Dev).ToArray()
            });

            TopDevXAxes.Add(new Axis());
            TopDevYAxes.Add(new Axis { Labels = top.Select(x => x.Label).ToArray() });

            // ---- Bank-Bestände ----
            BankSeries.Clear();
            BankXAxes.Clear();
            BankYAxes.Clear();

            var banks = _db.LadeGeldinstituteMitSaldo(DateTime.Today);

            BankSeries.Add(new ColumnSeries<double>
            {
                Name = "Saldo",
                Values = banks.Select(b => (double)b.Schlussaldo).ToArray()
            });

            // Verbesserung 1 auch hier: gedrehte Labels
            BankXAxes.Add(new Axis { Labels = banks.Select(b => b.Name).ToArray(), LabelsRotation = 60, TextSize = 12 });
            BankYAxes.Add(new Axis());
        }

        // ===== kleine Hilfsklassen =====

        public sealed class GroupingOption
        {
            public GroupingOption(string key, string label) { Key = key; Label = label; }
            public string Key { get; }
            public string Label { get; }
        }

        public sealed class NumberRangeVm : BaseViewModel
        {
            public NumberRangeVm(int start, int end, string display)
            {
                Start = start;
                End = end;
                Display = display;
            }

            public int Start { get; }
            public int End { get; }
            public string Display { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(); }
            }
        }
    }
}
