using MyCoinFlow.Models;
using MyCoinFlow.Services;
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
    public partial class SetVerteilenDialog : Window, INotifyPropertyChanged
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
                // CH-tolerant: "1'234.50" / "1234,50" / "1234.50"
                var s = (_betragText ?? "").Trim();
                s = s.Replace("’", "'").Replace(" ", "");
                s = s.Replace("'", "");      // Tausender
                s = s.Replace(",", ".");     // Dezimal

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    Betrag = val;
                else
                    Betrag = 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // ===== Helper for rounded allocation =====
        private sealed class RawShare
        {
            public int EigentuemerId { get; set; }
            public decimal BetragRaw { get; set; }
        }

        // ===== Energie: Diff Anzeige je Zähler =====
        public sealed class EnergieDiffRowVm : INotifyPropertyChanged
        {
            public int ZaehlerId { get; init; }
            public string Typ { get; init; } = "";
            public string Name { get; init; } = "";
            public int? EinheitId { get; init; }

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

        // ===== Collections =====
        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();

        // NEU: Zählerdaten-Sets (Dropdown) + Diff-Grid
        public ObservableCollection<ZaehlerdatenSetVm> ZaehlerdatenSets { get; } = new();

        public ObservableCollection<EnergieDiffRowVm> EnergieDiffRows { get; } = new();

        // ===== Selection =====
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


        // ===== Status / Flags =====
        public bool IsEditable => !_set.IsClosed;
        public bool IsReadOnlyGrid => _set.IsClosed;

        // Set-Typ aus DB: IsCredit (Single Source of Truth)
        private bool IsCreditSet => _set.IsCredit;

        // Signed Total (Belastung = +, Gutschrift = -)
        private decimal SetTotalSigned => IsCreditSet ? -Math.Abs(_set.Betrag) : Math.Abs(_set.Betrag);

        private bool _isEnergyVisible;
        public bool IsEnergyVisible
        {
            get => _isEnergyVisible;
            private set { _isEnergyVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUseStandardAuto)); }
        }

        // FIX/MEA Buttons deaktivieren, wenn ENERGIE aktiv
        public bool CanUseStandardAuto => IsEditable && !IsEnergyVisible;

        public string HeaderText { get; private set; } = "";

        public string StatusLine
        {
            get
            {
                var status = _set.IsClosed ? "Status: GESCHLOSSEN" : "Status: OFFEN";
                var typ = IsCreditSet ? "Typ: GUTSCHRIFT" : "Typ: BELASTUNG";
                return $"{status}  |  {typ}";
            }
        }

        public string TotalText => $"Total: {FormatChf(SetTotalSigned)}";
        public string DistributedText => $"Verteilt: {FormatChf(Rows.Sum(r => r.Betrag))}";
        public string RestText => $"Rest: {FormatChf(SetTotalSigned - Rows.Sum(r => r.Betrag))}";

        // ===== Energie Info-Texts für XAML =====
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

        // Cache: vorheriges Zählerdaten-Set (für Berechnung)
        private StweZaehlerdatenSet? _prevZaehlerdatenSet;

        public SetVerteilenDialog(StweSetRow setRow)
        {
            InitializeComponent();
            _set = setRow ?? throw new ArgumentNullException(nameof(setRow));

            HeaderText = $"{_set.Datum:yyyy-MM-dd}  |  {_set.Titel}";

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

        // ===== Load =====

        private void LoadOwners()
        {
            Owners.Clear();
            foreach (var o in _db.StweEigentuemerGetAll())
                Owners.Add(o);
        }

        private void LoadExistingLines()
        {
            Rows.Clear();

            // Merkt sich den "dominanten" Source, damit wir den Schlüssel vorauswählen können.
            // Wir ignorieren MANUELL, wenn es auch echte Schlüssel gibt.
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

            // Wenn bereits verteilt: bevorzugt echten Schlüssel (z.B. ENERGIE, MEA:..., FIX:...)
            if (Rows.Count > 0)
            {
                var best = sourceCount
                    .Where(kv => !string.Equals(kv.Key, "MANUELL", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .FirstOrDefault();

                // Fallback: wenn alles MANUELL ist, bleibt best null
                _lastUsedSourceFromRows = best;
            }
            else
            {
                _lastUsedSourceFromRows = null;
            }
        }


        private void LoadSchluessel()
        {
            Schluessel.Clear();
            foreach (var s in _db.StweSchluesselGetByLiegenschaft(_set.LiegenschaftId))
                Schluessel.Add(s);

            // Standard: MEA (meist verwendet), dann FIX, sonst erster
            // Wenn schon verteilt wurde: den verwendeten Schlüssel anzeigen (z.B. ENERGIE)
            // sonst Standard: MEA → FIX → erster
            if (!string.IsNullOrWhiteSpace(_lastUsedSourceFromRows))
            {
                // ENERGIE ist Modus, aber Source ist "ENERGIE"
                if (string.Equals(_lastUsedSourceFromRows, "ENERGIE", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedSchluessel = Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "ENERGIE", StringComparison.OrdinalIgnoreCase))
                                         ?? Schluessel.FirstOrDefault();
                }
                else if (_lastUsedSourceFromRows.StartsWith("MEA:", StringComparison.OrdinalIgnoreCase))
                {
                    // Source "MEA:<id>"
                    if (int.TryParse(_lastUsedSourceFromRows.Substring(4), out var id))
                        SelectedSchluessel = Schluessel.FirstOrDefault(x => x.Id == id) ?? Schluessel.FirstOrDefault();
                    else
                        SelectedSchluessel = Schluessel.FirstOrDefault();
                }
                else if (_lastUsedSourceFromRows.StartsWith("FIX:", StringComparison.OrdinalIgnoreCase))
                {
                    // Source "FIX:<id>"
                    if (int.TryParse(_lastUsedSourceFromRows.Substring(4), out var id))
                        SelectedSchluessel = Schluessel.FirstOrDefault(x => x.Id == id) ?? Schluessel.FirstOrDefault();
                    else
                        SelectedSchluessel = Schluessel.FirstOrDefault();
                }
                else
                {
                    // unbekannter Source -> Standard
                    SelectedSchluessel =
                        Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "MEA", StringComparison.OrdinalIgnoreCase))
                        ?? Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "FIX", StringComparison.OrdinalIgnoreCase))
                        ?? Schluessel.FirstOrDefault();
                }
            }
            else
            {
                SelectedSchluessel =
                    Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "MEA", StringComparison.OrdinalIgnoreCase))
                    ?? Schluessel.FirstOrDefault(x => string.Equals(x.Modus, "FIX", StringComparison.OrdinalIgnoreCase))
                    ?? Schluessel.FirstOrDefault();
            }

            UpdateEnergyVisibility();


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

                if (s.Id == oldest.Id)
                    label += " (erstes Set)";

                if (!s.RechnungKwhTotal.HasValue || s.RechnungKwhTotal.Value <= 0m)
                    label += " [kWh?]";

                // WICHTIG: Wir fügen jetzt ein ZaehlerdatenSetVm hinzu (nicht das DB-Objekt direkt)
                ZaehlerdatenSets.Add(new ZaehlerdatenSetVm
                {
                    Model = s,
                    DisplayText = label
                });
            }

            // Default-Auswahl: neustes Set, das <= Set-Datum ist (sonst neustes insgesamt)
            var best = ZaehlerdatenSets
                .Where(x => x.Model.ErfasstAm.Date <= _set.Datum.Date)
                .OrderByDescending(x => x.Model.ErfasstAm)
                .ThenByDescending(x => x.Model.Id)
                .FirstOrDefault();

            SelectedZaehlerdatenSet = best ?? ZaehlerdatenSets.FirstOrDefault();
        }


        private void UpdateEnergyVisibility()
        {
            IsEnergyVisible = string.Equals(SelectedSchluessel?.Modus, "ENERGIE", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshEnergieInfo()
        {
            EnergieDiffRows.Clear();
            _prevZaehlerdatenSet = null;

            if (!IsEnergyVisible)
            {
                EnergieZeitraumText = "Zeitraum: —";
                EnergieNotizText = "Notiz: —";
                EnergieRechnungKwhText = "—";
                EnergieGutschriftChfText = "—";
                return;
            }

            if (SelectedZaehlerdatenSet == null)
            {
                EnergieZeitraumText = "Zeitraum: —";
                EnergieNotizText = "Notiz: —";
                EnergieRechnungKwhText = "—";
                EnergieGutschriftChfText = "—";
                return;
            }

            var current = SelectedZaehlerdatenSet.Model;

            _prevZaehlerdatenSet = _db.StweZaehlerdatenGetPreviousSet(
                _set.LiegenschaftId,
                current.ErfasstAm,
                current.Id);

            var prevText = _prevZaehlerdatenSet == null ? "—" : _prevZaehlerdatenSet.ErfasstAm.ToString("dd.MM.yyyy");
            var curText = current.ErfasstAm.ToString("dd.MM.yyyy");
            EnergieZeitraumText = $"Zeitraum: {prevText} – {curText}";

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
            if (_prevZaehlerdatenSet != null)
            {
                prevLines = _db.StweZaehlerdatenLinesGetBySet(_prevZaehlerdatenSet.Id)
                    .ToDictionary(x => x.ZaehlerId, x => x.NeuWert);
            }

            foreach (var z in zaehler)
            {
                curLines.TryGetValue(z.Id, out var neu);
                prevLines.TryGetValue(z.Id, out var alt);

                EnergieDiffRows.Add(new EnergieDiffRowVm
                {
                    ZaehlerId = z.Id,
                    Typ = z.Typ,
                    Name = z.Name,
                    EinheitId = z.EinheitId,
                    AltWert = alt,
                    NeuWert = neu
                });
            }
        }


        // ===== Row events =====

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

        // ===== Buttons =====

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
            var raw = lines.Select(l =>
            {
                var amount = total * (l.AnteilProzent / 100m);
                return new RawShare { EigentuemerId = l.EigentuemerId, BetragRaw = amount };
            }).ToList();

            ApplyRoundedRows(raw, $"Auto (FIX): {SelectedSchluessel.Name}", $"FIX:{SelectedSchluessel.Id}");
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
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

        // ===== ENERGIE: Rechnen aus Zählerdaten-Set =====
        private void EnergieBerechnen_Click(object sender, RoutedEventArgs e)
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

            // Preis aus Rechnung-kWh (nicht EVU, nicht interne Summe!)
            var rechnungKwh = SelectedZaehlerdatenSet.Model.RechnungKwhTotal.Value;
            var preis = SetTotalSigned / rechnungKwh;

            // Diffs aus DiffRows (Alt/Neu aus Set und previous Set)
            if (EnergieDiffRows.Count == 0)
                RefreshEnergieInfo();

            // Gutschrift-Sonderfall: weiterhin vollständig nach MEA
            if (IsCreditSet)
            {
                ApplyEnergyAsMeaOnly();
                return;
            }

            // MEA Basis
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
                    "Energie berechnen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sumMea = ownerMea.Values.Sum();
            if (sumMea <= 0m)
            {
                MessageBox.Show("Summe MEA ist 0 – keine Verteilung möglich.",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sammeln (mit Detail)
            var directKwhByOwner = new Dictionary<int, decimal>();
            var directChfByOwner = new Dictionary<int, decimal>();
            var allgChfByOwner = new Dictionary<int, decimal>();
            var heizChfByOwner = new Dictionary<int, decimal>();

            void AddDict(Dictionary<int, decimal> dict, int key, decimal value)
            {
                if (!dict.ContainsKey(key)) dict[key] = 0m;
                dict[key] += value;
            }

            // DIREKT: Diff je Direkt-Zähler -> Eigentümer der Einheit am Set-Datum
            foreach (var d in EnergieDiffRows.Where(x => string.Equals(x.Typ, "DIREKT", StringComparison.OrdinalIgnoreCase)))
            {
                var diff = d.DiffKwh;
                if (diff <= 0m) continue;

                if (!d.EinheitId.HasValue)
                {
                    MessageBox.Show($"Direkt-Zähler „{d.Name}“ hat keine EinheitId (Stammdaten prüfen).",
                        "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var oid = _db.StweEigentuemerGetByEinheitAtDate(d.EinheitId.Value, _set.Datum);
                if (!oid.HasValue)
                {
                    MessageBox.Show($"Für die Einheit des Zählers „{d.Name}“ ist am Datum ({_set.Datum:yyyy-MM-dd}) kein Eigentümer zugeordnet.",
                        "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var chf = diff * preis;
                AddDict(directKwhByOwner, oid.Value, diff);
                AddDict(directChfByOwner, oid.Value, chf);
            }

            // ALLG/HEIZ total kWh
            var kwhAllg = EnergieDiffRows.Where(x => string.Equals(x.Typ, "ALLG", StringComparison.OrdinalIgnoreCase))
                                        .Sum(x => Math.Max(0m, x.DiffKwh));
            var totalAllg = kwhAllg * preis;

            var kwhHeiz = EnergieDiffRows.Where(x => string.Equals(x.Typ, "HEIZ", StringComparison.OrdinalIgnoreCase))
                                        .Sum(x => Math.Max(0m, x.DiffKwh));
            var totalHeiz = kwhHeiz * preis;

            // MEA-Verteilung ALLG/HEIZ
            foreach (var kv in ownerMea)
            {
                var ownerId = kv.Key;
                var mea = kv.Value;

                if (kwhAllg > 0m)
                    AddDict(allgChfByOwner, ownerId, totalAllg * (mea / sumMea));

                if (kwhHeiz > 0m)
                    AddDict(heizChfByOwner, ownerId, totalHeiz * (mea / sumMea));
            }

            // Raw Sum pro Owner
            var ownerAmount = new Dictionary<int, decimal>();
            foreach (var oid in ownerMea.Keys.Union(directChfByOwner.Keys))
            {
                var sum = 0m;
                if (directChfByOwner.TryGetValue(oid, out var dchf)) sum += dchf;
                if (allgChfByOwner.TryGetValue(oid, out var achf)) sum += achf;
                if (heizChfByOwner.TryGetValue(oid, out var hchf)) sum += hchf;
                if (sum != 0m) ownerAmount[oid] = sum;
            }

            var sumOwner = ownerAmount.Values.Sum();
            if (sumOwner == 0m)
            {
                MessageBox.Show("Es konnte kein Betrag berechnet werden (Summe = 0).",
                    "Energie berechnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Skala auf exaktes SetTotalSigned (weil interne Diff-Summe nicht zwingend == RechnungkWh)
            var scale = SetTotalSigned / sumOwner;

            var raw = ownerAmount.Select(kv => new RawShare
            {
                EigentuemerId = kv.Key,
                BetragRaw = kv.Value * scale
            }).ToList();

            // Notizen pro Owner
            var ownerNotiz = new Dictionary<int, string>();
            foreach (var kv in ownerMea)
            {
                var oid = kv.Key;
                var mea = kv.Value;

                directKwhByOwner.TryGetValue(oid, out var dkwh);
                directChfByOwner.TryGetValue(oid, out var dchf);
                allgChfByOwner.TryGetValue(oid, out var achf);
                heizChfByOwner.TryGetValue(oid, out var hchf);
                ownerAmount.TryGetValue(oid, out var sumRaw);

                var parts =
                    $"Dir {dkwh:0.###}kWh×{preis:0.####}={dchf:0.00}"
                    + $" | Allg {kwhAllg:0.###}kWh×{preis:0.####}×(MEA {mea:0.###}/{sumMea:0.###})={achf:0.00}"
                    + $" | Heiz {kwhHeiz:0.###}kWh×{preis:0.####}×(MEA {mea:0.###}/{sumMea:0.###})={hchf:0.00}"
                    + $" | Sum={sumRaw:0.00}"
                    + (Math.Abs(scale - 1m) > 0.0000001m ? $" | Scale×{scale:0.######}" : "");

                ownerNotiz[oid] = parts;
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
                    "Energie berechnen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

        // ===== Rounded Rows =====

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

        // ===== Save =====

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditable) return;
            TrySave(showSuccessMessage: true);
        }

        private void SetVerteilenDialog_Closing(object? sender, CancelEventArgs e)
        {
            if (!IsEditable) return;

            // Auto-Save beim X
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
            // leeres Set ist ok
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

            // Vorzeichen-Regeln
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

            // Summen-Regel (Überverteilung verhindern) – für beide Vorzeichen korrekt
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

        // ===== Helpers =====

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
