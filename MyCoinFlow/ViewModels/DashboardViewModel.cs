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
                _selectedGrouping = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SelectAllRangesCommand { get; }
        public ICommand SelectNoneRangesCommand { get; }
        public ICommand ApplyFiltersCommand { get; }

        // ===== Sidebar =====

        private bool _showPercent;
        public bool ShowPercent
        {
            get => _showPercent;
            set { _showPercent = value; OnPropertyChanged(); }
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
            SelectedGrouping = GroupingOptions.FirstOrDefault(o => o.Key == "Gruppe") ?? GroupingOptions.First();

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

            LoadNumberRanges();
            LoadAll();
        }

        /// <summary>
        /// Lädt die Nummernkreise aus der DB (NumberRangeRules).
        /// </summary>
        private void LoadNumberRanges()
        {
            NumberRanges.Clear();

            // defensiv: falls Tabelle noch nicht existiert, wird sie bei LadeNummernRegeln angelegt
            var rules = _db.LadeNummernRegeln();

            foreach (var r in rules.OrderBy(x => x.RangeStart))
            {
                var label = !string.IsNullOrWhiteSpace(r.Bezeichnung)
                    ? r.Bezeichnung!
                    : $"{r.Richtung} {r.RangeStart}-{r.RangeEnd}";

                NumberRanges.Add(new NumberRangeVm(r.RangeStart, r.RangeEnd, label)
                {
                    IsSelected = true
                });
            }
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

            // Zeitraumlabel (einfach, stabil)
            ZeitraumLabel = "Aktiver Budgetzeitraum (falls vorhanden) – Stand: " + DateTime.Today.ToString("d", CultureInfo.GetCultureInfo("de-CH"));
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
                allAccounts = allAccounts
                    .Where(a => selectedRanges.Any(r => a.Kontonummer >= r.Start && a.Kontonummer <= r.End))
                    .ToList();
            }

            string key = SelectedGrouping?.Key ?? "Gruppe";

            string GroupKey(KontoplanEintrag a)
            {
                return key switch
                {
                    "Art" => a.Art ?? "",
                    "Untergruppe" => a.Untergruppe ?? "",
                    _ => a.Gruppe ?? ""
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

            XAxes.Add(new Axis { Labels = labels });
            YAxes.Add(new Axis());

            ColumnChartTitle = $"Budget vs. IST ({SelectedGrouping?.Label ?? "Gruppe"})";

            // ---- Pie: IST-Verteilung ----
            PieSeries.Clear();
            foreach (var g in groups.Where(x => x.Ist != 0))
            {
                PieSeries.Add(new PieSeries<double>
                {
                    Name = g.Label,
                    Values = new[] { (double)Math.Abs(g.Ist) }
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

            TopDevSeries.Add(new RowSeries<double> { Name = "Abweichung", Values = top.Select(x => x.Dev).ToArray() });
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
            BankXAxes.Add(new Axis { Labels = banks.Select(b => b.Name).ToArray() });
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
