using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class SetVerteilenDialog : BaseWindow, INotifyPropertyChanged
    {
        // ===== Row VM (Verteilzeilen) =====
        public sealed class RowVm : INotifyPropertyChanged
        {
            private int? _eigentuemerId;
            private string _betragText = "0.00";
            private decimal _betrag;
            private string? _notiz;
            private string _source = "MANUELL";

            public int? EigentuemerId
            {
                get => _eigentuemerId;
                set { _eigentuemerId = value; OnPropertyChanged(); }
            }

            public string BetragText
            {
                get => _betragText;
                set
                {
                    _betragText = value ?? "";
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
                set { _source = value ?? ""; OnPropertyChanged(); }
            }

            private void ParseBetrag()
            {
                var s = (_betragText ?? "").Trim();
                s = s.Replace("’", "'").Replace(" ", "");
                s = s.Replace("'", "");
                s = s.Replace(",", ".");

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    Betrag = val;
                else
                    Betrag = 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class RawShare
        {
            public int EigentuemerId { get; set; }
            public decimal BetragRaw { get; set; }
        }

        public sealed class EnergieDiffRowVm : INotifyPropertyChanged
        {
            public int ZaehlerId { get; init; }
            public string Typ { get; init; } = "";
            public string Name { get; init; } = "";
            public int? EinheitId { get; init; }
            public int? SchluesselId { get; init; }

            public decimal AltWert { get; init; }
            public decimal NeuWert { get; init; }
            public decimal DiffKwh => NeuWert - AltWert;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public sealed class ZaehlerdatenSetVm
        {
            public StweZaehlerdatenSet Model { get; init; } = null!;
            public string DisplayText { get; init; } = "";
        }

        private readonly DatabaseService _db = new();
        private readonly StweSetRow _set;

        private string? _lastUsedSourceFromRows;

        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();
        public ObservableCollection<ZaehlerdatenSetVm> ZaehlerdatenSets { get; } = new();
        public ObservableCollection<EnergieDiffRowVm> EnergieDiffRows { get; } = new();

        private StweSchluessel? _selectedSchluessel;
        public StweSchluessel? SelectedSchluessel
        {
            get => _selectedSchluessel;
            set
            {
                _selectedSchluessel = value;
                OnPropertyChanged();
                UpdateEnergyVisibility();
            }
        }

        private ZaehlerdatenSetVm? _selectedZaehlerdatenSet;
        public ZaehlerdatenSetVm? SelectedZaehlerdatenSet
        {
            get => _selectedZaehlerdatenSet;
            set { _selectedZaehlerdatenSet = value; OnPropertyChanged(); RefreshEnergieInfo(); }
        }

        public bool IsEditable => !_set.IsClosed;
        public bool IsReadOnlyGrid => _set.IsClosed;

        private bool IsCreditSet => _set.IsCredit;
        private decimal SetTotalSigned => IsCreditSet ? -Math.Abs(_set.Betrag) : Math.Abs(_set.Betrag);

        private bool _isEnergyVisible;
        public bool IsEnergyVisible
        {
            get => _isEnergyVisible;
            private set { _isEnergyVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUseStandardAuto)); }
        }

        public bool CanUseStandardAuto => IsEditable && !IsEnergyVisible;

        public string HeaderText { get; private set; } = "";

        public string StatusLine
        {
            get
            {
                var status = _set.IsClosed ? "Status: GESCHLOSSEN" : "Status: OFFEN";
                var typ = IsCreditSet ? "Typ: GUTSCHRIFT" : "Typ: BELASTUNG";
                var verteilDatum = $"Verteil-Stichtag: {_set.VerteilDatum:yyyy-MM-dd}";
                return $"{status}  |  {typ}  |  {verteilDatum}";
            }
        }

        public string TotalText => $"Total: {FormatChf(SetTotalSigned)}";
        public string DistributedText => $"Verteilt: {FormatChf(Rows.Sum(r => r.Betrag))}";
        public string RestText => $"Rest: {FormatChf(SetTotalSigned - Rows.Sum(r => r.Betrag))}";

        private string _energieZeitraumText = "Zeitraum: —";
        public string EnergieZeitraumText
        {
            get => _energieZeitraumText;
            private set { _energieZeitraumText = value; OnPropertyChanged(); }
        }

        private string _energieNotizText = "Notiz: —";
        public string EnergieNotizText
        {
            get => _energieNotizText;
            private set { _energieNotizText = value; OnPropertyChanged(); }
        }

        private string _energieRechnungKwhText = "—";
        public string EnergieRechnungKwhText
        {
            get => _energieRechnungKwhText;
            private set { _energieRechnungKwhText = value; OnPropertyChanged(); }
        }

        private string _energieGutschriftChfText = "—";
        public string EnergieGutschriftChfText
        {
            get => _energieGutschriftChfText;
            private set { _energieGutschriftChfText = value; OnPropertyChanged(); }
        }

        private StweZaehlerdatenSet? _prevZaehlerdatenSet;

        public SetVerteilenDialog(StweSetRow setRow)
        {
            InitializeComponent();

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    e.Handled = false;
            };

            _set = setRow ?? throw new ArgumentNullException(nameof(setRow));

            HeaderText = $"{_set.Datum:yyyy-MM-dd}  |  Verteil-Stichtag: {_set.VerteilDatum:yyyy-MM-dd}  |  {_set.Titel}";

            LoadOwners();
            LoadExistingLines();
            LoadSchluessel();
            LoadZaehlerdatenSets();

            Rows.CollectionChanged += Rows_CollectionChanged;
            Closing += SetVerteilenDialog_Closing;

            DataContext = this;

            RaiseTotals();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsReadOnlyGrid));
            OnPropertyChanged(nameof(StatusLine));
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

            var sourceCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var l in _db.StweSetLinesGet(_set.Id))
            {
                var src = string.IsNullOrWhiteSpace(l.Schluessel) ? "MANUELL" : l.Schluessel!.Trim();

                if (!sourceCount.ContainsKey(src))
                    sourceCount[src] = 0;

                sourceCount[src]++;

                var row = new RowVm
                {
                    EigentuemerId = l.EigentuemerId,
                    BetragText = l.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = l.Notiz,
                    Source = src
                };

                AttachRow(row);
                Rows.Add(row);
            }

            if (Rows.Count > 0)
            {
                _lastUsedSourceFromRows = sourceCount
                    .Where(kv => !string.Equals(kv.Key, "MANUELL", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .FirstOrDefault();
            }
            else
            {
                _lastUsedSourceFromRows = null;
            }
        }

        private void LoadSchluessel()
        {
            Schluessel.Clear();

            var energieUi = new StweSchluessel
            {
                Id = -1,
                LiegenschaftId = _set.LiegenschaftId,
                Name = "Energie (Zähler)",
                Modus = "ENERGIE"
            };

            Schluessel.Add(energieUi);

            foreach (var s in _db.StweSchluesselGetByLiegenschaft(_set.LiegenschaftId))
                Schluessel.Add(s);

            if (!string.IsNullOrWhiteSpace(_lastUsedSourceFromRows))
            {
                if (string.Equals(_lastUsedSourceFromRows, "ENERGIE", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedSchluessel = energieUi;
                }
                else if (_lastUsedSourceFromRows.StartsWith("MEA:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(_lastUsedSourceFromRows.Substring(4), out var id))
                        SelectedSchluessel = Schluessel.FirstOrDefault(x => x.Id == id) ?? Schluessel.FirstOrDefault();
                    else
                        SelectedSchluessel = Schluessel.FirstOrDefault();
                }
                else if (_lastUsedSourceFromRows.StartsWith("FIX:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(_lastUsedSourceFromRows.Substring(4), out var id))
                        SelectedSchluessel = Schluessel.FirstOrDefault(x => x.Id == id) ?? Schluessel.FirstOrDefault();
                    else
                        SelectedSchluessel = Schluessel.FirstOrDefault();
                }
                else
                {
                    SelectedSchluessel =
                        Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "MEA", StringComparison.OrdinalIgnoreCase))
                        ?? Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "FIX", StringComparison.OrdinalIgnoreCase))
                        ?? Schluessel.FirstOrDefault(x => x.Id != -1)
                        ?? energieUi;
                }
            }
            else
            {
                SelectedSchluessel =
                    Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "MEA", StringComparison.OrdinalIgnoreCase))
                    ?? Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "FIX", StringComparison.OrdinalIgnoreCase))
                    ?? Schluessel.FirstOrDefault(x => x.Id != -1)
                    ?? energieUi;
            }

            UpdateEnergyVisibility();
        }

        private void LoadZaehlerdatenSets()
        {
            ZaehlerdatenSets.Clear();
            SelectedZaehlerdatenSet = null;

            var sets = _db.StweZaehlerdatenSetsGetByLiegenschaft(_set.LiegenschaftId)
                          .OrderByDescending(x => x.ErfasstAm)
                          .ThenByDescending(x => x.Id)
                          .ToList();

            if (sets.Count == 0)
                return;

            var oldest = sets.OrderBy(x => x.ErfasstAm).ThenBy(x => x.Id).First();

            foreach (var s in sets)
            {
                var date = s.ErfasstAm.ToString("dd.MM.yyyy");
                var notiz = (s.Notiz ?? "").Trim();

                var label = string.IsNullOrWhiteSpace(notiz) ? date : $"{date} – {notiz}";

                if (s.ErfassungsTyp == 1)
                {
                    if (s.MonatsAnzahl.HasValue && s.MonatsAnzahl.Value > 0)
                        label += $" [Monatswerte, {s.MonatsAnzahl.Value} Monate]";
                    else
                        label += " [Monatswerte]";
                }
                else
                {
                    label += " [Differenz]";
                }

                if (s.Id == oldest.Id)
                    label += " (erstes Set)";

                if (!s.RechnungKwhTotal.HasValue || s.RechnungKwhTotal.Value <= 0m)
                    label += " [kWh?]";

                ZaehlerdatenSets.Add(new ZaehlerdatenSetVm
                {
                    Model = s,
                    DisplayText = label
                });
            }

            int? savedId = null;

            try
            {
                savedId = _db.StweSetGetEnergieZaehlerdatenSetId(_set.Id);
            }
            catch
            {
            }

            if (savedId.HasValue)
            {
                SelectedZaehlerdatenSet = ZaehlerdatenSets.FirstOrDefault(x => x.Model.Id == savedId.Value)
                                          ?? ZaehlerdatenSets.FirstOrDefault();
                return;
            }

            var best = ZaehlerdatenSets
                .Where(x => x.Model.ErfasstAm.Date <= _set.VerteilDatum.Date)
                .OrderByDescending(x => x.Model.ErfasstAm)
                .ThenByDescending(x => x.Model.Id)
                .FirstOrDefault();

            SelectedZaehlerdatenSet = best ?? ZaehlerdatenSets.FirstOrDefault();
        }

        private void UpdateEnergyVisibility()
        {
            IsEnergyVisible = string.Equals(SelectedSchluessel?.Modus?.Trim(), "ENERGIE", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshEnergieInfo()
        {
            EnergieDiffRows.Clear();
            _prevZaehlerdatenSet = null;

            if (!IsEnergyVisible || SelectedZaehlerdatenSet == null)
            {
                EnergieZeitraumText = "Zeitraum: —";
                EnergieNotizText = "Notiz: —";
                EnergieRechnungKwhText = "—";
                EnergieGutschriftChfText = "—";
                return;
            }

            var current = SelectedZaehlerdatenSet.Model;
            var isMonatswertSet = current.ErfassungsTyp == 1;

            if (!isMonatswertSet)
            {
                _prevZaehlerdatenSet = _db.StweZaehlerdatenGetPreviousSet(
                    _set.LiegenschaftId,
                    current.ErfasstAm,
                    current.Id);
            }

            if (isMonatswertSet)
            {
                var monateText = current.MonatsAnzahl.HasValue && current.MonatsAnzahl.Value > 0
                    ? $" ({current.MonatsAnzahl.Value} Monat(e))"
                    : "";

                EnergieZeitraumText = $"Zeitraum: Monatswerte bis {current.ErfasstAm:dd.MM.yyyy}{monateText}";
            }
            else
            {
                var prevText = _prevZaehlerdatenSet == null ? "—" : _prevZaehlerdatenSet.ErfasstAm.ToString("dd.MM.yyyy");
                var curText = current.ErfasstAm.ToString("dd.MM.yyyy");
                EnergieZeitraumText = $"Zeitraum: {prevText} – {curText}";
            }

            var n = (current.Notiz ?? "").Trim();
            EnergieNotizText = string.IsNullOrWhiteSpace(n) ? "Notiz: —" : $"Notiz: {n}";

            EnergieRechnungKwhText = current.RechnungKwhTotal.HasValue
                ? current.RechnungKwhTotal.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "—";

            EnergieGutschriftChfText = current.GutschriftChf.HasValue
                ? current.GutschriftChf.Value.ToString("0.00", CultureInfo.InvariantCulture)
                : "—";

            var zaehler = _db.StweZaehlerGetByLiegenschaft(_set.LiegenschaftId);

            var curLines = _db.StweZaehlerdatenLinesGetBySet(current.Id)
                .ToDictionary(x => x.ZaehlerId, x => x.NeuWert);

            var prevLines = new Dictionary<int, decimal>();

            if (!isMonatswertSet && _prevZaehlerdatenSet != null)
            {
                prevLines = _db.StweZaehlerdatenLinesGetBySet(_prevZaehlerdatenSet.Id)
                    .ToDictionary(x => x.ZaehlerId, x => x.NeuWert);
            }

            foreach (var z in zaehler)
            {
                curLines.TryGetValue(z.Id, out var neu);

                decimal alt = 0m;
                if (!isMonatswertSet)
                    prevLines.TryGetValue(z.Id, out alt);

                EnergieDiffRows.Add(new EnergieDiffRowVm
                {
                    ZaehlerId = z.Id,
                    Typ = z.Typ,
                    Name = z.Name,
                    EinheitId = z.EinheitId,
                    AltWert = alt,
                    NeuWert = neu,
                    SchluesselId = z.SchluesselId
                });
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
            if (e.PropertyName == nameof(RowVm.BetragText) || e.PropertyName == nameof(RowVm.Betrag))
                RaiseTotals();
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            Rows.Add(new RowVm
            {
                EigentuemerId = null,
                BetragText = IsCreditSet ? "-0.00" : "0.00",
                Source = "MANUELL"
            });
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            if (Grid.SelectedItem is RowVm row)
                Rows.Remove(row);
        }

        private void ClearRows_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            Rows.Clear();
            RaiseTotals();
        }

        private void AutoFix_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            if (SelectedSchluessel == null)
            {
                MessageBox.Show("Bitte zuerst einen Schlüssel auswählen.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.Equals(SelectedSchluessel.Modus, "FIX", StringComparison.OrdinalIgnoreCase))
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

            var total = SetTotalSigned;

            var rawByOwner = new Dictionary<int, decimal>();
            var missing = new List<string>();

            foreach (var l in lines)
            {
                var ownerId = ResolveOwnerFromEinheitOrFallback(l.EinheitId, l.EigentuemerId);

                if (!ownerId.HasValue)
                {
                    missing.Add(!string.IsNullOrWhiteSpace(l.EinheitBezeichnung)
                        ? l.EinheitBezeichnung
                        : l.EigentuemerName);
                    continue;
                }

                var amount = total * (l.AnteilProzent / 100m);

                if (!rawByOwner.ContainsKey(ownerId.Value))
                    rawByOwner[ownerId.Value] = 0m;

                rawByOwner[ownerId.Value] += amount;
            }

            if (missing.Count > 0)
            {
                MessageBox.Show(
                    $"Für folgende Einheiten ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " +
                    string.Join("\n• ", missing.Take(10)) +
                    (missing.Count > 10 ? "\n…" : ""),
                    "Auto verteilen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var raw = rawByOwner
                .Select(kv => new RawShare { EigentuemerId = kv.Key, BetragRaw = kv.Value })
                .ToList();

            ApplyRoundedRows(raw, $"Auto (FIX): {SelectedSchluessel.Name}", $"FIX:{SelectedSchluessel.Id}");
        }


        private int? ResolveOwnerFromEinheitOrFallback(int? einheitId, int fallbackEigentuemerId)
        {
            if (einheitId.HasValue && einheitId.Value > 0)
            {
                var ownerAtDate = _db.StweEigentuemerGetByEinheitAtDate(einheitId.Value, _set.VerteilDatum);
                if (ownerAtDate.HasValue)
                    return ownerAtDate.Value;

                return null;
            }

            return fallbackEigentuemerId > 0 ? fallbackEigentuemerId : null;
        }





        private void AutoMea_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            if (SelectedSchluessel == null)
            {
                MessageBox.Show("Bitte zuerst einen Schlüssel auswählen.", "Auto verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.Equals(SelectedSchluessel.Modus, "MEA", StringComparison.OrdinalIgnoreCase))
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

            var ownerMea = new Dictionary<int, decimal>();
            var missing = new List<string>();

            foreach (var u in units)
            {
                var oid = _db.StweEigentuemerGetByEinheitAtDate(u.Id, _set.VerteilDatum);

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
                    $"Für folgende Einheiten ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " +
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
                MessageBox.Show("Summe MEA ist 0 – keine Verteilung möglich.",
                    "Auto verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var total = SetTotalSigned;

            var raw = ownerMea.Select(kv =>
            {
                var amount = total * (kv.Value / sumMea);
                return new RawShare { EigentuemerId = kv.Key, BetragRaw = amount };
            }).ToList();

            ApplyRoundedRows(raw, $"Auto (MEA): {SelectedSchluessel.Name}", $"MEA:{SelectedSchluessel.Id}");
        }

        void EnergieBerechnen_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;

            if (!IsEnergyVisible)
            {
                MessageBox.Show("Bitte zuerst den Schlüssel „ENERGIE“ wählen.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedZaehlerdatenSet == null)
            {
                MessageBox.Show("Bitte zuerst ein Zählerdaten-Set auswählen.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!SelectedZaehlerdatenSet.Model.RechnungKwhTotal.HasValue || SelectedZaehlerdatenSet.Model.RechnungKwhTotal.Value <= 0m)
            {
                MessageBox.Show("Im Zählerdaten-Set fehlt „Rechnung kWh total“.\n\nBitte unter „Zählerdaten“ nachtragen.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _db.StweSetUpdateEnergieZaehlerdatenSetId(_set.Id, SelectedZaehlerdatenSet.Model.Id);
            }
            catch
            {
            }

            if (Rows.Count > 0)
            {
                var res = MessageBox.Show(
                    "Die Energie-Berechnung ersetzt die bestehenden Verteilzeilen.\n\nMöchtest du fortfahren?",
                    "Energie berechnen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                    return;
            }

            var rechnungKwh = SelectedZaehlerdatenSet.Model.RechnungKwhTotal.Value;
            var preis = SetTotalSigned / rechnungKwh;

            if (EnergieDiffRows.Count == 0)
                RefreshEnergieInfo();

            if (IsCreditSet)
            {
                ApplyEnergyAsMeaOnly();
                return;
            }

            var units = _db.StweEinheitenGetByLiegenschaft(_set.LiegenschaftId)
                           .Where(u => u.MeaPromille.HasValue && u.MeaPromille.Value > 0m)
                           .ToList();

            if (units.Count == 0)
            {
                MessageBox.Show("Keine Einheiten mit MEA (‰) gefunden.\n\nBitte MEA bei den Einheiten erfassen.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ownerMea = new Dictionary<int, decimal>();
            var missing = new List<string>();

            foreach (var u in units)
            {
                var oid = _db.StweEigentuemerGetByEinheitAtDate(u.Id, _set.VerteilDatum);

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
                    $"Für folgende Einheiten ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " +
                    string.Join("\n• ", missing.Take(10)) +
                    (missing.Count > 10 ? "\n…" : "") +
                    "\n\nBitte unter „Liegenschaften → Eigentümer & Zuordnung“ nachpflegen.",
                    "Energie berechnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sumMea = ownerMea.Values.Sum();

            if (sumMea <= 0m)
            {
                MessageBox.Show("Summe MEA ist 0 – keine Verteilung möglich.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ownerAmountRaw = new Dictionary<int, decimal>();
            var ownerNotizParts = new Dictionary<int, List<string>>();

            void AddAmount(int ownerId, decimal value, string notizPart)
            {
                if (!ownerAmountRaw.ContainsKey(ownerId))
                    ownerAmountRaw[ownerId] = 0m;

                ownerAmountRaw[ownerId] += value;

                if (!ownerNotizParts.ContainsKey(ownerId))
                    ownerNotizParts[ownerId] = new List<string>();

                ownerNotizParts[ownerId].Add(notizPart);
            }

            foreach (var d in EnergieDiffRows)
            {
                var diff = d.DiffKwh;
                if (diff <= 0m) continue;

                if (string.Equals(d.Typ?.Trim(), "EVU", StringComparison.OrdinalIgnoreCase)) continue;

                var chfTotalForZaehler = diff * preis;

                var typ = (d.Typ ?? "").Trim().ToUpperInvariant();

                if (typ == "DIREKT")
                {
                    if (!d.EinheitId.HasValue || d.EinheitId.Value <= 0)
                    {
                        MessageBox.Show(
                            $"DIREKT-Zähler „{d.Name}“ hat keine Einheit.",
                            "Energie berechnen",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    var ownerId = _db.StweEigentuemerGetByEinheitAtDate(d.EinheitId.Value, _set.VerteilDatum);

                    if (!ownerId.HasValue)
                    {
                        MessageBox.Show(
                            $"Für DIREKT-Zähler „{d.Name}“ ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet.",
                            "Energie berechnen",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    AddAmount(
                        ownerId.Value,
                        chfTotalForZaehler,
                        $"{d.Name}: DIREKT {diff:0.###}kWh × {preis:0.####} = {chfTotalForZaehler:0.00}"
                    );

                    continue;
                }

                if (typ == "ALLG" || typ == "HEIZ")
                {
                    foreach (var kv in ownerMea)
                    {
                        var part = chfTotalForZaehler * (kv.Value / sumMea);

                        AddAmount(
                            kv.Key,
                            part,
                            $"{d.Name}: {typ} {diff:0.###}kWh × {preis:0.####} × MEA {kv.Value:0.###}/{sumMea:0.###} = {part:0.00}"
                        );
                    }

                    continue;
                }

                MessageBox.Show(
                    $"Unbekannter Zählertyp „{d.Typ}“ bei Zähler „{d.Name}“.",
                    "Energie berechnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sumOwner = ownerAmountRaw.Values.Sum();

            if (sumOwner == 0m)
            {
                MessageBox.Show("Es konnte kein Betrag berechnet werden (Summe = 0).",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var scale = SetTotalSigned / sumOwner;

            var raw = ownerAmountRaw.Select(kv => new RawShare
            {
                EigentuemerId = kv.Key,
                BetragRaw = kv.Value * scale
            }).ToList();

            var ownerNotiz = new Dictionary<int, string>();

            foreach (var kv in ownerNotizParts)
            {
                var oid = kv.Key;
                var parts = kv.Value;

                var sumRawForOwner = ownerAmountRaw.TryGetValue(oid, out var s) ? s : 0m;

                var note =
                    string.Join(" | ", parts.Take(6)) +
                    (parts.Count > 6 ? " | …" : "") +
                    $" | Sum={sumRawForOwner:0.00}" +
                    (Math.Abs(scale - 1m) > 0.0000001m ? $" | Scale×{scale:0.######}" : "");

                ownerNotiz[oid] = note;
            }

            ApplyRoundedRows(raw, ownerNotiz, "ENERGIE");
        }

        private void ApplyEnergyAsMeaOnly()
        {
            var units = _db.StweEinheitenGetByLiegenschaft(_set.LiegenschaftId)
                           .Where(u => u.MeaPromille.HasValue && u.MeaPromille.Value > 0m)
                           .ToList();

            if (units.Count == 0)
            {
                MessageBox.Show("Keine Einheiten mit MEA (‰) gefunden.\n\nBitte MEA bei den Einheiten erfassen.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ownerMea = new Dictionary<int, decimal>();
            var missing = new List<string>();

            foreach (var u in units)
            {
                var oid = _db.StweEigentuemerGetByEinheitAtDate(u.Id, _set.VerteilDatum);

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
                    $"Für folgende Einheiten ist am Verteil-Stichtag ({_set.VerteilDatum:yyyy-MM-dd}) kein Eigentümer zugeordnet:\n\n• " +
                    string.Join("\n• ", missing.Take(10)) +
                    (missing.Count > 10 ? "\n…" : "") +
                    "\n\nBitte unter „Liegenschaften → Eigentümer & Zuordnung“ nachpflegen.",
                    "Energie berechnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sumMea = ownerMea.Values.Sum();

            if (sumMea <= 0m)
            {
                MessageBox.Show("Summe MEA ist 0 – keine Verteilung möglich.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var total = SetTotalSigned;

            var raw = ownerMea.Select(kv =>
            {
                var amount = total * (kv.Value / sumMea);
                return new RawShare { EigentuemerId = kv.Key, BetragRaw = amount };
            }).ToList();

            var ownerNotiz = ownerMea.ToDictionary(
                kv => kv.Key,
                kv => $"MEA {kv.Value:0.###}/{sumMea:0.###} → {total:0.00}");

            ApplyRoundedRows(raw, ownerNotiz, "ENERGIE");
        }

        private void ApplyRoundedRows(List<RawShare> raw, string notiz, string source)
        {
            var rounded = raw
                .Select(x => new
                {
                    x.EigentuemerId,
                    Betrag = Math.Round(x.BetragRaw, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            var sumRounded = rounded.Sum(x => x.Betrag);
            var diff = SetTotalSigned - sumRounded;

            if (diff != 0m && rounded.Count > 0)
            {
                var idx = rounded
                    .Select((x, i) => new { Abs = Math.Abs(x.Betrag), Index = i })
                    .OrderByDescending(x => x.Abs)
                    .First().Index;

                var item = rounded[idx];
                rounded[idx] = new { item.EigentuemerId, Betrag = item.Betrag + diff };
            }

            Rows.Clear();

            foreach (var r in rounded)
            {
                Rows.Add(new RowVm
                {
                    EigentuemerId = r.EigentuemerId,
                    BetragText = r.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = notiz,
                    Source = source
                });
            }

            RaiseTotals();
        }

        private void ApplyRoundedRows(List<RawShare> raw, Dictionary<int, string> ownerNotiz, string source)
        {
            var rounded = raw
                .Select(x => new
                {
                    x.EigentuemerId,
                    Betrag = Math.Round(x.BetragRaw, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            var sumRounded = rounded.Sum(x => x.Betrag);
            var diff = SetTotalSigned - sumRounded;

            if (diff != 0m && rounded.Count > 0)
            {
                var idx = rounded
                    .Select((x, i) => new { Abs = Math.Abs(x.Betrag), Index = i })
                    .OrderByDescending(x => x.Abs)
                    .First().Index;

                var item = rounded[idx];
                rounded[idx] = new { item.EigentuemerId, Betrag = item.Betrag + diff };

                if (ownerNotiz != null && ownerNotiz.TryGetValue(item.EigentuemerId, out var old))
                    ownerNotiz[item.EigentuemerId] = $"{old} | Diff {diff:+0.00;-0.00;0.00}";
            }

            Rows.Clear();

            foreach (var r in rounded)
            {
                ownerNotiz ??= new();
                ownerNotiz.TryGetValue(r.EigentuemerId, out var n);

                Rows.Add(new RowVm
                {
                    EigentuemerId = r.EigentuemerId,
                    BetragText = r.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = string.IsNullOrWhiteSpace(n) ? null : n,
                    Source = source
                });
            }

            RaiseTotals();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;
            TrySave(showSuccessMessage: true);
        }

        private void SetVerteilenDialog_Closing(object? sender, CancelEventArgs e)
        {
            if (!IsEditable) return;

            if (!TrySave())
                e.Cancel = true;
        }

        private bool TrySave(bool showSuccessMessage = false)
        {
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

        private bool ValidateBeforeSave()
        {
            if (Rows.Count == 0) return true;

            if (Rows.Any(r => !r.EigentuemerId.HasValue || r.EigentuemerId.Value <= 0))
            {
                MessageBox.Show("Bitte in allen Zeilen einen Eigentümer wählen.", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.GroupBy(r => r.EigentuemerId!.Value).Any(g => g.Count() > 1))
            {
                MessageBox.Show("Ein Eigentümer darf im Set nur einmal vorkommen (V1).", "Set verteilen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (IsCreditSet)
            {
                if (Rows.Any(r => r.Betrag > 0m))
                {
                    MessageBox.Show("Bei Gutschriften müssen die Zeilenbeträge negativ sein.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                if (Rows.Any(r => r.Betrag < 0m))
                {
                    MessageBox.Show("Bei Belastungen dürfen die Zeilenbeträge nicht negativ sein.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            var sum = Rows.Sum(r => r.Betrag);
            var total = SetTotalSigned;
            const decimal eps = 0.0001m;

            if (!IsCreditSet)
            {
                if (sum > total + eps)
                {
                    MessageBox.Show("Summe der Zeilen darf den Set-Betrag nicht überschreiten.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                if (sum < total - eps)
                {
                    MessageBox.Show("Summe der Zeilen darf den (negativen) Set-Betrag nicht unterschreiten.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
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
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(DistributedText));
            OnPropertyChanged(nameof(RestText));
            OnPropertyChanged(nameof(StatusLine));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}