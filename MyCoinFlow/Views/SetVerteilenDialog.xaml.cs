using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class SetVerteilenDialog : Window, INotifyPropertyChanged
    {
        private sealed class RawShare
        {
            public int EigentuemerId { get; set; }
            public string Name { get; set; } = "";
            public decimal BetragRaw { get; set; }
        }

        public sealed class RowVm : INotifyPropertyChanged
        {
            private int? _eigentuemerId;
            private string _eigentuemerName = "";
            private string _betragText = "0.00";
            private decimal _betrag;
            private string? _notiz;
            private string _source = "MANUELL";

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

            public string Source
            {
                get => _source;
                set { _source = value; OnPropertyChanged(); }
            }

            private void ParseBetrag()
            {
                var raw = (_betragText ?? "").Trim().Replace(" ", "").Replace(",", ".");
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    Betrag = val;
                else
                    Betrag = 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly DatabaseService _db = new();
        private readonly StweSetRow _set;

        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();

        private StweSchluessel? _selectedSchluessel;
        public StweSchluessel? SelectedSchluessel
        {
            get => _selectedSchluessel;
            set { _selectedSchluessel = value; OnPropertyChanged(); }
        }

        // ---- Closed handling ----
        public bool IsEditable => !_set.IsClosed;
        public bool IsReadOnlyGrid => _set.IsClosed;

        public string StatusLine => _set.IsClosed ? "Status: GESCHLOSSEN" : "Status: OFFEN";

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
            LoadSchluessel();
            LoadExistingLines();

            Rows.CollectionChanged += Rows_CollectionChanged;
            Closing += SetVerteilenDialog_Closing;

            DataContext = this;

            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsReadOnlyGrid));
            OnPropertyChanged(nameof(StatusLine));

            RaiseTotals();
        }

        private void LoadOwners()
        {
            Owners.Clear();
            foreach (var o in _db.StweEigentuemerGetAll())
                Owners.Add(o);
        }

        private void LoadSchluessel()
        {
            Schluessel.Clear();
            foreach (var s in _db.StweSchluesselGetByLiegenschaft(_set.LiegenschaftId))
                Schluessel.Add(s);

            SelectedSchluessel = Schluessel.FirstOrDefault();
        }

        private void LoadExistingLines()
        {
            Rows.Clear();

            foreach (var l in _db.StweSetLinesGet(_set.Id))
            {
                var owner = l.EigentuemerId.HasValue ? Owners.FirstOrDefault(x => x.Id == l.EigentuemerId.Value) : null;

                var row = new RowVm
                {
                    EigentuemerId = l.EigentuemerId,
                    EigentuemerName = owner?.Name ?? "",
                    BetragText = l.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = l.Notiz,
                    Source = string.IsNullOrWhiteSpace(l.Schluessel) ? "MANUELL" : l.Schluessel!
                };

                AttachRow(row);
                Rows.Add(row);
            }
        }

        private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var it in e.NewItems)
                    if (it is RowVm r) AttachRow(r);

            if (e.OldItems != null)
                foreach (var it in e.OldItems)
                    if (it is RowVm r) DetachRow(r);

            RaiseTotals();
        }

        private void AttachRow(RowVm r) => r.PropertyChanged += Row_PropertyChanged;
        private void DetachRow(RowVm r) => r.PropertyChanged -= Row_PropertyChanged;

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (sender is RowVm row)
                {
                    if (e.PropertyName == nameof(RowVm.EigentuemerId))
                    {
                        var o = row.EigentuemerId.HasValue ? Owners.FirstOrDefault(x => x.Id == row.EigentuemerId.Value) : null;
                        row.EigentuemerName = o?.Name ?? "";
                    }

                    if (e.PropertyName == nameof(RowVm.BetragText) || e.PropertyName == nameof(RowVm.Betrag))
                        RaiseTotals();
                }
            }
            catch { /* still */ }
        }

        // -------- Buttons / Actions --------

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            Rows.Add(new RowVm
            {
                EigentuemerId = null,
                EigentuemerName = "",
                BetragText = "0.00",
                Source = "MANUELL"
            });
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            if (Grid.SelectedItem is RowVm row)
                Rows.Remove(row);
        }

        private void ClearRows_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            Rows.Clear();
            RaiseTotals();
        }

        private void AutoFix_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            if (SelectedSchluessel == null)
            {
                MessageBox.Show("Bitte zuerst einen Schlüssel auswählen.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSchluessel.Modus != "FIX")
            {
                MessageBox.Show("Dieser Schlüssel ist nicht FIX.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lines = _db.StweSchluesselLinesGet(SelectedSchluessel.Id);
            if (lines.Count == 0)
            {
                MessageBox.Show("Dieser FIX-Schlüssel hat noch keine Zeilen.\n\nBitte unter „Liegenschaften → Schlüssel“ erfassen.",
                    "Auto verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sumPct = lines.Sum(x => x.AnteilProzent);
            if (Math.Abs((double)(sumPct - 100m)) > 0.0001)
            {
                MessageBox.Show($"Schlüssel ist ungültig: Summe ist {sumPct:N4}% (muss 100.0000% sein).",
                    "Auto verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var raw = lines
                .Select(l =>
                {
                    var owner = Owners.FirstOrDefault(o => o.Id == l.EigentuemerId);
                    var name = owner?.Name ?? l.EigentuemerName ?? "";
                    var amount = _set.Betrag * (l.AnteilProzent / 100m);

                    return new RawShare
                    {
                        EigentuemerId = l.EigentuemerId,
                        Name = name,
                        BetragRaw = amount
                    };
                })
                .ToList();

            ApplyRoundedRows(raw, $"Auto (FIX): {SelectedSchluessel.Name}", $"FIX:{SelectedSchluessel.Id}");
        }

        private void AutoMea_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            if (SelectedSchluessel == null)
            {
                MessageBox.Show("Bitte zuerst einen Schlüssel auswählen.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSchluessel.Modus != "MEA")
            {
                MessageBox.Show("Dieser Schlüssel ist nicht MEA.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var units = _db.StweEinheitenGetByLiegenschaft(_set.LiegenschaftId)
                           .Where(u => u.MeaPromille.HasValue && u.MeaPromille.Value > 0m)
                           .ToList();

            if (units.Count == 0)
            {
                MessageBox.Show("Keine Einheiten mit MEA (‰) gefunden.\n\nBitte MEA bei den Einheiten erfassen.",
                    "Auto verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ownerMea = new System.Collections.Generic.Dictionary<int, decimal>();
            var missing = new System.Collections.Generic.List<string>();

            foreach (var u in units)
            {
                var oid = _db.StweEigentuemerGetByEinheitAtDate(u.Id, _set.Datum);
                if (!oid.HasValue)
                {
                    missing.Add(u.Bezeichnung);
                    continue;
                }

                if (!ownerMea.ContainsKey(oid.Value))
                    ownerMea[oid.Value] = 0m;

                ownerMea[oid.Value] += (u.MeaPromille ?? 0m);
            }

            if (missing.Count > 0)
            {
                MessageBox.Show(
                    $"Für folgende Einheiten ist am Transaktionsdatum ({_set.Datum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " +
                    string.Join("\n• ", missing.Take(10)) +
                    (missing.Count > 10 ? "\n…" : "") +
                    "\n\nBitte unter „Liegenschaften → Eigentümer & Zuordnung“ nachpflegen.",
                    "Auto verteilen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sumMea = ownerMea.Values.Sum();
            if (sumMea <= 0m)
            {
                MessageBox.Show("Summe MEA ist 0 – keine Verteilung möglich.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var raw = ownerMea
                .Select(kv =>
                {
                    var oid = kv.Key;
                    var mea = kv.Value;

                    var owner = Owners.FirstOrDefault(o => o.Id == oid);
                    var name = owner?.Name ?? $"Eigentümer #{oid}";

                    var amount = _set.Betrag * (mea / sumMea);

                    return new RawShare
                    {
                        EigentuemerId = oid,
                        Name = name,
                        BetragRaw = amount
                    };
                })
                .ToList();

            ApplyRoundedRows(raw, $"Auto (MEA): {SelectedSchluessel.Name}", $"MEA:{SelectedSchluessel.Id}");
        }

        private void ApplyRoundedRows(System.Collections.Generic.List<RawShare> raw, string notiz, string source)
        {
            var rounded = raw
                .Select(x => new
                {
                    x.EigentuemerId,
                    x.Name,
                    Betrag = Math.Round(x.BetragRaw, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            var sumRounded = rounded.Sum(x => x.Betrag);
            var diff = _set.Betrag - sumRounded;

            if (diff != 0m && rounded.Count > 0)
            {
                var idx = rounded
                    .Select((x, i) => new { x.Betrag, Index = i })
                    .OrderByDescending(x => x.Betrag)
                    .First().Index;

                var item = rounded[idx];
                rounded[idx] = new { item.EigentuemerId, item.Name, Betrag = item.Betrag + diff };
            }

            Rows.Clear();
            foreach (var r in rounded)
            {
                Rows.Add(new RowVm
                {
                    EigentuemerId = r.EigentuemerId,
                    EigentuemerName = r.Name,
                    BetragText = r.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = notiz,
                    Source = source
                });
            }

            RaiseTotals();
        }

        // ---- Save / Close / Closing ----

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;
            TrySave(showSuccessMessage: true);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Schließen immer möglich
            if (_set.IsClosed) { Close(); return; }

            if (TrySave())
                Close();
        }

        private void CloseSet_Click(object sender, RoutedEventArgs e)
        {
            if (_set.IsClosed) return;

            if (!TrySave())
                return;

            var rest = _set.Betrag - Rows.Sum(r => r.Betrag);
            if (Math.Abs((double)rest) > 0.0001)
            {
                MessageBox.Show("Set kann nur abgeschlossen werden, wenn Rest = 0.00 ist.",
                    "Abschließen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _db.StweSetSetClosed(_set.Id, true);
            MessageBox.Show("Set wurde abgeschlossen.", "Abschließen",
                MessageBoxButton.OK, MessageBoxImage.Information);

            _set.IsClosed = true;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsReadOnlyGrid));
            OnPropertyChanged(nameof(StatusLine));

            Close();
        }

        private void SetVerteilenDialog_Closing(object? sender, CancelEventArgs e)
        {
            // Wenn geschlossen -> nie blockieren
            if (_set.IsClosed) return;

            // Auto-Save beim X
            if (!TrySave())
                e.Cancel = true;
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
                        schluessel: r.Source,
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
                var o = r.EigentuemerId.HasValue ? Owners.FirstOrDefault(x => x.Id == r.EigentuemerId.Value) : null;
                r.EigentuemerName = o?.Name ?? "";
            }
        }

        private bool ValidateBeforeSave()
        {
            if (Rows.Count == 0) return true;

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

            if (Rows.GroupBy(r => r.EigentuemerId!.Value).Any(g => g.Count() > 1))
            {
                MessageBox.Show("Ein Eigentümer darf im Set nur einmal vorkommen (V1).", "Set verteilen",
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
