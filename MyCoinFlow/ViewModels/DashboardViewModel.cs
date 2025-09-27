using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LiveChartsCore;                    // ISeries, Axis
using LiveChartsCore.SkiaSharpView;      // ColumnSeries<double>, PieSeries<double>, RowSeries<double>, Axis
using MyCoinFlow.Services.Dashboard;
using MyCoinFlow.Services;               // DatabaseService
using MyCoinFlow.Models;                 // für GeldinstitutSaldo
using LiveChartsCore.Measure;                  // DataLabelsPosition
using LiveChartsCore.SkiaSharpView.Painting;   // SolidColorPaint
using SkiaSharp;                               // SKColors



namespace MyCoinFlow.ViewModels
{
    public sealed class DashboardViewModel : BaseViewModel
    {
        private readonly IDashboardDataProvider _provider;
        private readonly CancellationTokenSource _cts = new();

        public DashboardViewModel(IDashboardDataProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

            GroupingOptions = new ObservableCollection<GroupingOption>
            {
                new("Kontenart",          GroupingDimension.KontenArt),
                new("Kontengruppe",       GroupingDimension.KontenGruppe),
                new("Kontenuntergruppe",  GroupingDimension.KontenUnterGruppe)
            };
            _selectedGrouping = GroupingOptions.First();

            RefreshCommand = new MyCoinFlow.Helpers.RelayCommand(_ => _ = LoadAsync());
        }

        #region Bindings: Gruppierung, KPIs, Charts

        public ObservableCollection<GroupingOption> GroupingOptions { get; }
        private GroupingOption _selectedGrouping;
        public GroupingOption SelectedGrouping
        {
            get => _selectedGrouping;
            set
            {
                if (_selectedGrouping == value) return;
                _selectedGrouping = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColumnChartTitle));
                _ = LoadAsync();
            }
        }

        // KPIs
        private int _openImportCount;
        public int OpenImportCount { get => _openImportCount; private set { _openImportCount = value; OnPropertyChanged(); } }

        private int _bankImportItemCount;
        public int BankImportItemCount { get => _bankImportItemCount; private set { _bankImportItemCount = value; OnPropertyChanged(); } }

        // Budget/IST Summen
        private decimal _budgetGesamt;
        public decimal BudgetGesamt
        {
            get => _budgetGesamt;
            private set { _budgetGesamt = value; OnPropertyChanged(); OnPropertyChanged(nameof(Differenz)); }
        }

        private decimal _istGesamt;
        public decimal IstGesamt
        {
            get => _istGesamt;
            private set { _istGesamt = value; OnPropertyChanged(); OnPropertyChanged(nameof(Differenz)); }
        }

        public decimal Differenz => BudgetGesamt - IstGesamt;

        private string _zeitraumLabel = "Zeitraum: (aktiv)";
        public string ZeitraumLabel { get => _zeitraumLabel; private set { _zeitraumLabel = value; OnPropertyChanged(); } }

        public string ColumnChartTitle => $"Budget vs. IST nach {SelectedGrouping.Label}";

        // Charts: Budget vs IST, Pie
        public ObservableCollection<ISeries> ColumnSeries { get; } = new();
        public ObservableCollection<ISeries> PieSeries { get; } = new();

        private Axis[] _xAxes = new[] { new Axis { Labels = Array.Empty<string>(), LabelsRotation = 0 } };
        public Axis[] XAxes { get => _xAxes; private set { _xAxes = value; OnPropertyChanged(); } }

        private Axis[] _yAxes = new[] { new Axis { Labeler = v => v.ToString("N2") } };
        public Axis[] YAxes { get => _yAxes; private set { _yAxes = value; OnPropertyChanged(); } }

        // Chart: Bank-Bestände (horizontal)
        public ObservableCollection<ISeries> BankSeries { get; } = new();
        private Axis[] _bankXAxes = new[] { new Axis { Labeler = v => v.ToString("N2") } }; // numerisch (Saldo)
        public Axis[] BankXAxes { get => _bankXAxes; private set { _bankXAxes = value; OnPropertyChanged(); } }

        private Axis[] _bankYAxes = new[] { new Axis { Labels = Array.Empty<string>() } };   // Institutsnamen
        public Axis[] BankYAxes { get => _bankYAxes; private set { _bankYAxes = value; OnPropertyChanged(); } }

        public ICommand RefreshCommand { get; }

        #endregion

        #region Lifecycle

        public Task InitializeAsync() => LoadAsync();

        private bool _isBusy;
        private async Task LoadAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            try
            {
                // 1) Budget/IST-Daten vom Provider (gruppiert)
                var data = await _provider.LoadAsync(SelectedGrouping.Dimension, _cts.Token);

                BudgetGesamt = data.Points?.Sum(p => p.Budget) ?? 0m;
                IstGesamt = data.Points?.Sum(p => p.Ist) ?? 0m;
                ZeitraumLabel = (_provider as IWithPeriodInfo)?.PeriodInfo ?? "Zeitraum: aktiv";

                BuildBudgetIstCharts(data);

                // 2) KPIs + Bankbestände direkt aus DatabaseService (einfach & robust)
                LoadKpisAndBanks();

                OnPropertyChanged(nameof(ColumnChartTitle));
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                ColumnSeries.Clear();
                PieSeries.Clear();
                BankSeries.Clear();
                ZeitraumLabel = $"Fehler beim Laden: {ex.Message}";
                BudgetGesamt = IstGesamt = 0m;
                OpenImportCount = BankImportItemCount = 0;
            }
            finally { _isBusy = false; }
        }

        #endregion

        #region Build UI Models

        private void BuildBudgetIstCharts(DashboardData data)
        {
            var points = data?.Points ?? Array.Empty<DashboardPoint>();

            // 1) Art-Mapping je Label laden (aus Kontenplan)
            var db = new DatabaseService();
            var labelColumn = SelectedGrouping.Dimension switch
            {
                GroupingDimension.KontenArt => "Art",
                GroupingDimension.KontenGruppe => "Gruppe",
                GroupingDimension.KontenUnterGruppe => "Untergruppe",
                _ => "Art"
            };
            var artMap = db.LadeArtFlagProLabel(labelColumn); // label -> true(Einnahmen)/false(Ausgaben)/null

            // 2) Reihenfolge: Einnahmen (0) -> Ausgaben (1); innerhalb: Budget DESC
            int OrderKey(string? lbl)
            {
                if (lbl == null) return 1; // sicherheitshalber
                if (artMap.TryGetValue(lbl, out var inc))
                    return inc == true ? 0 : 1;  // null wird wie Ausgaben einsortiert
                return 1;
            }

            var ordered = points
                .OrderBy(p => OrderKey(p.Label))
                .ThenByDescending(p => p.Budget)
                .ToList();

            // 3) Labels & Werte (vier Arrays für 4 Serien)
            var labels = ordered.Select(p => p.Label ?? string.Empty).ToArray();
            var n = labels.Length;

            var budgetInc = new double[n];
            var istInc = new double[n];
            var budgetExp = new double[n];
            var istExp = new double[n];

            for (int i = 0; i < n; i++)
            {
                var p = ordered[i];
                var isIncome = artMap.TryGetValue(p.Label ?? string.Empty, out var inc) && inc == true;

                if (isIncome)
                {
                    budgetInc[i] = (double)p.Budget;
                    istInc[i] = (double)p.Ist;
                }
                else
                {
                    budgetExp[i] = (double)p.Budget;
                    istExp[i] = (double)p.Ist;
                }
            }

            // 4) Achsen (X = Labels)
            XAxes = new[]
            {
                new LiveChartsCore.SkiaSharpView.Axis
             {
            Labels = labels,
            LabelsRotation = labels.Any(l => !string.IsNullOrEmpty(l) && l.Length > 12) ? 15 : 0
             }
            };

            // 5) Serien setzen – Farben je Art und Serie
            //    Einnahmen: Grün; Ausgaben: Orange/Rot
            ColumnSeries.Clear();

            ColumnSeries.Add(new LiveChartsCore.SkiaSharpView.ColumnSeries<double>
            {
                Name = "Budget (Einnahmen)",
                Values = budgetInc,
                Fill = new SolidColorPaint(SKColors.SeaGreen)
            });

            ColumnSeries.Add(new LiveChartsCore.SkiaSharpView.ColumnSeries<double>
            {
                Name = "IST (Einnahmen)",
                Values = istInc,
                Fill = new SolidColorPaint(SKColors.MediumSeaGreen)
            });

            ColumnSeries.Add(new LiveChartsCore.SkiaSharpView.ColumnSeries<double>
            {
                Name = "Budget (Ausgaben)",
                Values = budgetExp,
                Fill = new SolidColorPaint(SKColors.OrangeRed)
            });

            ColumnSeries.Add(new LiveChartsCore.SkiaSharpView.ColumnSeries<double>
            {
                Name = "IST (Ausgaben)",
                Values = istExp,
                Fill = new SolidColorPaint(SKColors.Tomato)
            });

            // 6) Pie-Chart (IST-Verteilung) lassen wir unverändert – optional könntest du dort
            //    die Slices ebenfalls je Art einfärben; sag Bescheid, wenn du das möchtest.
            PieSeries.Clear();
            foreach (var p in points.Where(x => x.Ist != 0m))
            {
                PieSeries.Add(new LiveChartsCore.SkiaSharpView.PieSeries<double>
                {
                    Name = p.Label,
                    Values = new[] { (double)p.Ist },
                    Pushout = 0
                });
            }
        }



        private void LoadKpisAndBanks()
        {
            var db = new DatabaseService();

            // KPIs
            OpenImportCount = db.CountCreditCardStaging();
            BankImportItemCount = db.CountBankImportItem();

            // Bank-Bestände (per heutigem Datum)
            var banks = db.LadeGeldinstituteMitSaldo(DateTime.Today);
            if (banks == null) banks = new System.Collections.Generic.List<GeldinstitutSaldo>();

            // Labels + Werte
            var labels = banks.Select(b => string.IsNullOrWhiteSpace(b.Name) ? $"ID {b.Id}" : b.Name).ToArray();
            var values = banks.Select(b => (double)b.Schlussaldo).ToArray();

            // Achsen: Y = Banknamen, X = Betrag
            BankYAxes = new[] { new LiveChartsCore.SkiaSharpView.Axis { Labels = labels } };
            BankXAxes = new[] { new LiveChartsCore.SkiaSharpView.Axis { Labeler = v => v.ToString("N2") } };

            // Serie (horizontale Balken) + Werte INSIDE (Data Labels)
            BankSeries.Clear();
            BankSeries.Add(new LiveChartsCore.SkiaSharpView.RowSeries<double>
            {
                Name = "Saldo",
                Values = values,

                // Data Labels (im Balken)
                DataLabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.White),
                DataLabelsSize = 13,
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
                DataLabelsFormatter = point => point.Model.ToString("N2")   // <- hier: Model statt PrimaryValue
            });
        }



        #endregion

        #region Helper

        public sealed record GroupingOption(string Label, GroupingDimension Dimension);

        #endregion
    }
}
