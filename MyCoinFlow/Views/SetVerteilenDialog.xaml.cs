using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class SetVerteilenDialog : Window, INotifyPropertyChanged
    {
        public sealed class RowVm : INotifyPropertyChanged
        {
            private int? _eigentuemerId;
            private string _eigentuemerName = "";
            private string _betragText = "0.00";
            private decimal _betrag;
            private string? _notiz;

            public int? EigentuemerId
            {
                get => _eigentuemerId;
                set { _eigentuemerId = value; OnPropertyChanged(); }
            }

            public string EigentuemerName
            {
                get => _eigentuemerName;
                set { _eigentuemerName = value; OnPropertyChanged(); }
            }

            /// <summary>
            /// UI-Eingabe (string) – erlaubt Punkt UND Komma
            /// </summary>
            public string BetragText
            {
                get => _betragText;
                set
                {
                    _betragText = value;
                    OnPropertyChanged();
                    ParseBetrag();
                }
            }

            /// <summary>
            /// Rechenwert (decimal) – wird aus BetragText abgeleitet
            /// </summary>
            public decimal Betrag
            {
                get => _betrag;
                private set { _betrag = value; OnPropertyChanged(); }
            }

            public string? Notiz
            {
                get => _notiz;
                set { _notiz = value; OnPropertyChanged(); }
            }

            private void ParseBetrag()
            {
                var raw = (_betragText ?? "")
                    .Trim()
                    .Replace(" ", "")
                    .Replace(",", ".");

                if (decimal.TryParse(
                        raw,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var val))
                {
                    Betrag = val;
                }
                else
                {
                    Betrag = 0m;
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly DatabaseService _db = new();
        private readonly StweSetRow _set;

        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();

        public string HeaderText { get; private set; } = "";
        public string TotalText => $"Total: {FormatChf(_set.Betrag)}";
        public string DistributedText => $"Verteilt: {FormatChf(Rows.Sum(r => r.Betrag))}";
        public string RestText => $"Rest: {FormatChf(_set.Betrag - Rows.Sum(r => r.Betrag))}";

        public SetVerteilenDialog(StweSetRow setRow)
        {
            InitializeComponent();
            _set = setRow ?? throw new ArgumentNullException(nameof(setRow));

            HeaderText = $"{_set.Datum:yyyy-MM-dd}  |  {_set.Titel}";

            LoadOwners();
            LoadExistingLines();

            Rows.CollectionChanged += (_, e) =>
            {
                // neue Zeilen anhängen / entfernte lösen
                if (e.NewItems != null)
                    foreach (var it in e.NewItems)
                        if (it is RowVm r) AttachRow(r);

                if (e.OldItems != null)
                    foreach (var it in e.OldItems)
                        if (it is RowVm r) DetachRow(r);

                RaiseTotals();
            };
            Closing += SetVerteilenDialog_Closing;

            DataContext = this;
        }

        private void SetVerteilenDialog_Closing(object? sender, CancelEventArgs e)
        {
            // Wenn Auto-Save fehlschlägt (z.B. ungültige Daten),
            // verhindern wir das Schließen.
            if (!TrySave())
            {
                e.Cancel = true;
            }
        }


        private void AttachRow(RowVm r)
        {
            if (r == null) return;
            r.PropertyChanged += Row_PropertyChanged;
        }

        private void DetachRow(RowVm r)
        {
            if (r == null) return;
            r.PropertyChanged -= Row_PropertyChanged;
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (sender is RowVm row)
                {
                    // Wenn Eigentümer gewählt wurde -> Name sofort nachziehen
                    if (e.PropertyName == nameof(RowVm.EigentuemerId))
                    {
                        var o = row.EigentuemerId.HasValue
                            ? Owners.FirstOrDefault(x => x.Id == row.EigentuemerId.Value)
                            : null;

                        row.EigentuemerName = o?.Name ?? "";
                    }

                    // Beträge -> Totals live
                    if (e.PropertyName == nameof(RowVm.BetragText) || e.PropertyName == nameof(RowVm.Betrag))
                    {
                        RaiseTotals();
                    }
                }
            }
            catch
            {
                // still
            }
        }



        private void LoadOwners()
        {
            Owners.Clear();
            foreach (var o in _db.StweEigentuemerGetAll())
                Owners.Add(o);
        }

        private void LoadExistingLines()
        {
            Rows.Clear();

            foreach (var l in _db.StweSetLinesGet(_set.Id))
            {
                var owner = l.EigentuemerId.HasValue
                    ? Owners.FirstOrDefault(x => x.Id == l.EigentuemerId.Value)
                    : null;

                Rows.Add(new RowVm
                {
                    EigentuemerId = l.EigentuemerId,
                    EigentuemerName = owner?.Name ?? "",
                    BetragText = l.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = l.Notiz
                });
                AttachRow(Rows.Last());

            }

            RaiseTotals();
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            Rows.Add(new RowVm
            {
                EigentuemerId = null,
                EigentuemerName = "",
                BetragText = "0.00"
            });
            AttachRow(Rows.Last());
            RaiseTotals();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is RowVm row)
                Rows.Remove(row);
            RaiseTotals();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TrySave(showSuccessMessage: true);
        }



        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Auto-Save beim Schließen (damit nichts verloren geht)
            if (TrySave())
                Close();
        }

        private bool TrySave(bool showSuccessMessage = false)
        {
            SyncOwnerNames();

            if (!ValidateBeforeSave())
                return false;

            try
            {
                _db.StweSetLinesDeleteBySet(_set.Id);

                foreach (var r in Rows)
                {
                    _db.StweSetLineInsert(
                        setId: _set.Id,
                        einheitId: null,
                        eigentuemerId: r.EigentuemerId,
                        schluessel: "MANUELL",
                        betrag: r.Betrag,
                        notiz: r.Notiz
                    );
                }

                if (showSuccessMessage)
                {
                    MessageBox.Show("Verteilung gespeichert.", "Set verteilen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }


        private void SyncOwnerNames()
        {
            foreach (var r in Rows)
            {
                var o = r.EigentuemerId.HasValue
                    ? Owners.FirstOrDefault(x => x.Id == r.EigentuemerId.Value)
                    : null;

                r.EigentuemerName = o?.Name ?? "";
            }
        }

        private bool ValidateBeforeSave()
        {
            if (Rows.Count == 0)
            {
                MessageBox.Show("Bitte mindestens eine Zeile erfassen.", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.Any(r => !r.EigentuemerId.HasValue || r.EigentuemerId.Value <= 0))
            {
                MessageBox.Show("Bitte in allen Zeilen einen Eigentümer wählen.", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.Any(r => r.Betrag < 0m))
            {
                MessageBox.Show("Beträge dürfen nicht negativ sein.", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var sum = Rows.Sum(r => r.Betrag);
            if (sum > _set.Betrag + 0.0001m)
            {
                MessageBox.Show("Summe der Zeilen darf den Set-Betrag nicht überschreiten.", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private static string FormatChf(decimal v)
        {
            var ch = CultureInfo.GetCultureInfo("de-CH");
            return v.ToString("C", ch);
        }

        private void RaiseTotals()
        {
            OnPropertyChanged(nameof(DistributedText));
            OnPropertyChanged(nameof(RestText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
