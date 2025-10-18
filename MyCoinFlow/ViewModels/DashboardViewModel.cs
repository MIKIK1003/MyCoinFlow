using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Measure;
using SkiaSharp;
using MyCoinFlow.Services;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        #region ctor + init
        public DashboardViewModel()
        {
            // Gruppierungsvorgaben
            GroupingOptions = new ObservableCollection<GroupingOption>
    {
        new GroupingOption { Id = "Art",         Label = "Art" },
        new GroupingOption { Id = "Gruppe",      Label = "Gruppe" },
        new GroupingOption { Id = "Untergruppe", Label = "Untergruppe" }
    };

            // Standard: Untergruppe
            SelectedGrouping = GroupingOptions.FirstOrDefault(x => x.Id == "Untergruppe")
                               ?? GroupingOptions.First();

            // Commands
            RefreshCommand = new RelayCommand(_ => Apply());
            SelectAllRangesCommand = new RelayCommand(_ => { foreach (var r in NumberRanges) r.IsSelected = true; OnPropertyChanged(nameof(NumberRanges)); });
            SelectNoneRangesCommand = new RelayCommand(_ => { foreach (var r in NumberRanges) r.IsSelected = false; OnPropertyChanged(nameof(NumberRanges)); });
            ApplyFiltersCommand = new RelayCommand(_ => Apply());

            // Achsen/Serien Defaults
            XAxes = new List<Axis> { new Axis { Labels = Array.Empty<string>() } };
            YAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };
            BankYAxes = new List<Axis> { new Axis { Labels = Array.Empty<string>() } };
            BankXAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };
            TopDevYAxes = new List<Axis> { new Axis { Labels = Array.Empty<string>() } };
            TopDevXAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };
            ColumnSeries = Array.Empty<ISeries>();
            PieSeries = Array.Empty<ISeries>();
            BankSeries = Array.Empty<ISeries>();
            TopDevSeries = Array.Empty<ISeries>();

            // Vorgabe: Anzeige in % aktiv
            ShowPercent = true;

            // Nummernkreise laden (Defaults kommen in LoadNumberRanges)
            LoadNumberRanges();

            // Initial berechnen
            Apply();
        }

        #endregion

        #region sidebar state
        public ObservableCollection<RangeFilterItem> NumberRanges { get; } = new();

        private bool _showPercent;
        public bool ShowPercent
        {
            get => _showPercent;
            set { _showPercent = value; OnPropertyChanged(nameof(ShowPercent)); }
        }

        public ObservableCollection<GroupingOption> GroupingOptions { get; }
        private GroupingOption _selectedGrouping;
        public GroupingOption SelectedGrouping
        {
            get => _selectedGrouping;
            set { _selectedGrouping = value; OnPropertyChanged(nameof(SelectedGrouping)); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SelectAllRangesCommand { get; }
        public ICommand SelectNoneRangesCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        #endregion

        #region charts + kpis bindables
        private string _columnChartTitle = "Budget vs. IST";
        public string ColumnChartTitle
        {
            get => _columnChartTitle;
            set { _columnChartTitle = value; OnPropertyChanged(nameof(ColumnChartTitle)); }
        }

        public IEnumerable<ISeries> ColumnSeries { get; private set; }
        public IEnumerable<ISeries> PieSeries { get; private set; }
        public IEnumerable<ISeries> BankSeries { get; private set; }

        // NEU: Top-Abweichungen
        public IEnumerable<ISeries> TopDevSeries { get; private set; }
        public List<Axis> TopDevXAxes { get; private set; }
        public List<Axis> TopDevYAxes { get; private set; }

        public List<Axis> XAxes { get; private set; }
        public List<Axis> YAxes { get; private set; }
        public List<Axis> BankXAxes { get; private set; }
        public List<Axis> BankYAxes { get; private set; }

        private string _zeitraumLabel = "";
        public string ZeitraumLabel
        {
            get => _zeitraumLabel;
            set { _zeitraumLabel = value; OnPropertyChanged(nameof(ZeitraumLabel)); }
        }

        private int _openImportCount;
        public int OpenImportCount
        {
            get => _openImportCount;
            set { _openImportCount = value; OnPropertyChanged(nameof(OpenImportCount)); }
        }

        private int _bankImportItemCount;
        public int BankImportItemCount
        {
            get => _bankImportItemCount;
            set { _bankImportItemCount = value; OnPropertyChanged(nameof(BankImportItemCount)); }
        }
        #endregion

        #region loading + building

        private void LoadNumberRanges()
        {
            NumberRanges.Clear();

            try
            {
                var rules = _db.LadeNummernRegeln(); // liefert u.a. Bezeichnung/Richtung/RangeStart/RangeEnd

                foreach (var r in rules)
                {
                    // Anzeige-Text stabil aufbauen
                    var display = Sanitize(r.Bezeichnung) ?? $"{r.RangeStart}–{r.RangeEnd}";

                    NumberRanges.Add(new RangeFilterItem
                    {
                        Id = r.Id,
                        From = r.RangeStart,
                        To = r.RangeEnd,
                        Direction = r.Richtung ?? "",
                        Display = display,
                        // Wichtig: NICHT nach "Richtung" selektieren!
                        // Nur die echte Kategorie "Ausgaben" vorwählen.
                        IsSelected = IsAusgabenLabel(display)
                    });
                }

                // Failsafe: sicherstellen, dass höchstens EIN Eintrag selektiert ist.
                // Preferenz: exakter Name "Ausgaben", sonst erste passende Ausgaben-Kategorie.
                if (NumberRanges.Any())
                {
                    var selected = NumberRanges.Where(n => n.IsSelected).ToList();

                    if (selected.Count == 0)
                    {
                        var firstAusgaben = NumberRanges.FirstOrDefault(n => IsAusgabenLabel(n.Display));
                        if (firstAusgaben != null) firstAusgaben.IsSelected = true;
                    }
                    else if (selected.Count > 1)
                    {
                        var exact = NumberRanges.FirstOrDefault(n =>
                            string.Equals(n.Display?.Trim(), "Ausgaben", StringComparison.OrdinalIgnoreCase));

                        var keep = exact ?? selected.First();
                        foreach (var n in NumberRanges) n.IsSelected = ReferenceEquals(n, keep);
                    }
                }

                OnPropertyChanged(nameof(NumberRanges));
            }
            catch
            {
                // defensiv: leer lassen
            }
        }

        private static bool IsAusgabenLabel(string? label)
        {
            var s = (label ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return false;

            // explizit ausschließen:
            if (s.Contains("invest")) return false;        // Investitionen
            if (s.Contains("amort")) return false;         // Amortisationen
            if (s.Contains("durchlauf")) return false;     // Durchlaufkonten

            // nur "Ausgaben" zulassen (Ausgabe/Ausgaben)
            return s.StartsWith("ausgab");
        }



        private static string? Sanitize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            var txt = s.Trim();
            var idx = txt.IndexOf('(');
            if (idx > 0) txt = txt.Substring(0, idx).Trim();
            return txt;
        }

        private IEnumerable<KontoplanEintrag> FilterKontenByRanges(IEnumerable<KontoplanEintrag> src)
        {
            var active = NumberRanges.Where(n => n.IsSelected).ToList();

            // WICHTIG: Keine Auswahl => LEER
            if (active.Count == 0)
                return Enumerable.Empty<KontoplanEintrag>();

            return src.Where(k =>
            {
                var nr = k.Kontonummer;
                foreach (var r in active)
                    if (nr >= r.From && nr <= r.To) return true;
                return false;
            });
        }

        private void Apply()
        {
            // Zeitraum-Label
            try
            {
                var zList = _db.LadeBudgetzeitraeume();
                var active = zList.FirstOrDefault(z => z.IstAktiv);
                ZeitraumLabel = active != null
                    ? $"Zeitraum: {active.Bezeichnung} ({active.Startdatum:d} – {active.Enddatum:d})"
                    : "Zeitraum: (kein aktiver Zeitraum)";
            }
            catch { ZeitraumLabel = ""; }

            BuildKpis();
            BuildCharts();
            BuildBanks();
        }

        private void BuildKpis()
        {
            try { OpenImportCount = _db.CountCreditCardStaging(); } catch { OpenImportCount = 0; }
            try { BankImportItemCount = _db.CountBankImportItem(); } catch { BankImportItemCount = 0; }
        }

        private void BuildCharts()
        {
            // --- Daten laden + filtern ---
            List<KontoplanEintrag> all;
            try { all = _db.LadeKontenplan(); }
            catch { all = new List<KontoplanEintrag>(); }

            var filtered = FilterKontenByRanges(all).ToList();

            // Gruppierungsschlüssel
            Func<KontoplanEintrag, string> keySel = SelectedGrouping?.Id switch
            {
                "Gruppe" => k => string.IsNullOrWhiteSpace(k.Gruppe) ? "—" : k.Gruppe,
                "Untergruppe" => k => string.IsNullOrWhiteSpace(k.Untergruppe) ? "—" : k.Untergruppe,
                _ => k => string.IsNullOrWhiteSpace(k.Art) ? "—" : k.Art
            };
            ColumnChartTitle = $"{SelectedGrouping?.Label ?? "Art"} – Budget vs. IST";

            // Aggregation je Label
            var groups = filtered
                .GroupBy(keySel)
                .Select(g => new
                {
                    Key = g.Key,
                    Budget = g.Sum(x => x.Budgetwert ?? 0m),
                    Ist = g.Sum(x => x.Gebucht)
                })
                .OrderByDescending(x => Math.Abs(x.Budget))
                .ToList();

            // Kleiner Helfer: lange Kategorienamen kürzen (nur Anzeige)
            static string Short(string? s, int max = 22)
            {
                var t = (s ?? "").Trim();
                return (t.Length > max) ? t.Substring(0, max - 1) + "…" : t;
            }

            // ---------------- (1) Säulen: Budget vs. IST ----------------
            var labelsFull = groups.Select(g => g.Key).ToArray();
            var labelsShort = labelsFull.Select(l => Short(l)).ToArray();
            var budgetVals = groups.Select(g => (double)g.Budget).ToArray();
            var istVals = groups.Select(g => (double)g.Ist).ToArray();

            // ACHTUNG: vertikale Spalten -> Beschriftung auf X-Achse
            // Rotation = 90°, kleinere Textgröße und etwas Padding für bessere Lesbarkeit
            XAxes = new List<Axis>
            {
            new Axis
            {
            Labels = labelsShort,
            LabelsRotation = 60, // vertikale Beschriftung
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 16,
            // optional: etwas Achsen-Padding gegen Rand-Clipping
            Padding = new LiveChartsCore.Drawing.Padding(0, 12, 0, 0)
            }
            };

            YAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };

            ColumnSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Budget", Values = budgetVals },
        new ColumnSeries<double> { Name = "IST",    Values = istVals    }
            };
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
            OnPropertyChanged(nameof(ColumnSeries));

            // --------------- (2) Pie: Verteilung IST -------------------
            // NEU: absteigend nach Betrag sortieren -> Reihenfolge der Series
            var slices = groups
                .Select(g => new { g.Key, Val = Math.Abs(g.Ist) })
                .Where(x => x.Val > 0)
                .OrderByDescending(x => x.Val) // << Sortierung für Segmente & Legende
                .ToList();

            var total = slices.Sum(s => s.Val);
            var pie = new List<ISeries>();
            foreach (var s in slices)
            {
                string title = s.Key;
                if (ShowPercent && total > 0)
                {
                    var pct = s.Val / total;
                    title = $"{s.Key} ({pct:P0})";
                }

                pie.Add(new PieSeries<double>
                {
                    Name = title,
                    Values = new[] { (double)s.Val },
                    InnerRadius = 50
                });
            }
            PieSeries = pie;
            OnPropertyChanged(nameof(PieSeries));

            // --------- (3) Top-Abweichungen (unten links, unverändert) ----------
            var top = groups
                .Select(x => new
                {
                    x.Key,
                    x.Budget,
                    x.Ist,
                    Dev = x.Ist - x.Budget,
                    DevAbs = Math.Abs(x.Ist - x.Budget)
                })
                .OrderByDescending(x => x.DevAbs)
                .Take(8)
                .ToList();

            var topLabels = top.Select(t => t.Key).ToArray();
            var topValues = top.Select(t => (double)t.Dev).ToArray();

            TopDevYAxes = new List<Axis> { new Axis { Labels = topLabels } };
            TopDevXAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };

            TopDevSeries = new ISeries[]
            {
        new RowSeries<double>
        {
            Name = "Abweichung (IST − Budget)",
            Values = topValues,
            DataLabelsPaint    = new SolidColorPaint(SKColors.White),
            DataLabelsSize     = 13,
            DataLabelsPosition = DataLabelsPosition.Middle,
            DataLabelsFormatter = p => p.Model.ToString("N2")
        }
            };
            OnPropertyChanged(nameof(TopDevYAxes));
            OnPropertyChanged(nameof(TopDevXAxes));
            OnPropertyChanged(nameof(TopDevSeries));
        }


        private void BuildBanks()
        {
            List<GeldinstitutSaldo> banks;
            try { banks = _db.LadeGeldinstituteMitSaldo(DateTime.Today); }
            catch { banks = new List<GeldinstitutSaldo>(); }

            var labels = banks
                .Select(b => string.IsNullOrWhiteSpace(b.Name) ? $"ID {b.Id}" : b.Name)
                .ToArray();

            var values = banks
                .Select(b => (double)b.Schlussaldo)
                .ToArray();

            BankYAxes = new List<Axis> { new Axis { Labels = labels } };
            BankXAxes = new List<Axis> { new Axis { Labeler = v => v.ToString("N2") } };

            BankSeries = new ISeries[]
            {
                new RowSeries<double>
                {
                    Name = "Saldo",
                    Values = values,

                    // Werte IM Balken – ohne ChartPoint-Details
                    DataLabelsPaint    = new SolidColorPaint(SKColors.White),
                    DataLabelsSize     = 13,
                    DataLabelsPosition = DataLabelsPosition.Middle,
                    DataLabelsFormatter = p => p.Model.ToString("N2")
                }
            };

            OnPropertyChanged(nameof(BankYAxes));
            OnPropertyChanged(nameof(BankXAxes));
            OnPropertyChanged(nameof(BankSeries));
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion

        #region helper types
        public sealed class GroupingOption
        {
            public string Id { get; set; } = "";
            public string Label { get; set; } = "";
            public override string ToString() => Label;
        }

        public sealed class RangeFilterItem : INotifyPropertyChanged
        {
            public int Id { get; set; }
            public int From { get; set; }
            public int To { get; set; }
            public string Direction { get; set; } = "";
            public string Display { get; set; } = "";

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
        #endregion
    }
}
