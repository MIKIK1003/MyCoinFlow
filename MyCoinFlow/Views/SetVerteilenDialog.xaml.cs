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
        // ===== Row VM =====
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

        private readonly DatabaseService _db = new();
        private readonly StweSetRow _set;
        private bool _energieHadExisting = false;


        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();
        public ObservableCollection<StweSchluessel> Schluessel { get; } = new();
        public ObservableCollection<EnergieZaehlerVm> EnergieZaehler { get; } = new();


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

        // Status
        public bool IsEditable => !_set.IsClosed;
        public bool IsReadOnlyGrid => _set.IsClosed;

        private bool _isEnergyVisible;
        public bool IsEnergyVisible
        {
            get => _isEnergyVisible;
            private set { _isEnergyVisible = value; OnPropertyChanged(); }
        }


        // NEW: Set-Typ aus DB: IsCredit (Single Source of Truth)
        private bool IsCreditSet => _set.IsCredit;

        // Signed Total (Belastung = +, Gutschrift = -)
        private decimal SetTotalSigned => IsCreditSet ? -Math.Abs(_set.Betrag) : Math.Abs(_set.Betrag);

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

        public SetVerteilenDialog(StweSetRow setRow)
        {
            InitializeComponent();
            _set = setRow ?? throw new ArgumentNullException(nameof(setRow));

            HeaderText = $"{_set.Datum:yyyy-MM-dd}  |  {_set.Titel}";

            LoadOwners();
            LoadSchluessel();
            LoadExistingLines();
            LoadEnergieZaehlerDefaults();

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

        private void LoadSchluessel()
        {
            Schluessel.Clear();
            foreach (var s in _db.StweSchluesselGetByLiegenschaft(_set.LiegenschaftId))
                Schluessel.Add(s);

            SelectedSchluessel = Schluessel.FirstOrDefault();
            UpdateEnergyVisibility();

        }

        private void UpdateEnergyVisibility()
        {
            // Sichtbar nur wenn der gewählte Schlüssel den Modus "ENERGIE" hat
            IsEnergyVisible = string.Equals(SelectedSchluessel?.Modus, "ENERGIE", StringComparison.OrdinalIgnoreCase);
        }


        private void LoadExistingLines()

        {
            Rows.Clear();

            foreach (var l in _db.StweSetLinesGet(_set.Id))
            {
                var row = new RowVm
                {
                    EigentuemerId = l.EigentuemerId,
                    BetragText = l.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
                    Notiz = l.Notiz,
                    Source = string.IsNullOrWhiteSpace(l.Schluessel) ? "MANUELL" : l.Schluessel!
                };
                AttachRow(row);
                Rows.Add(row);
            }

        }

        // ===== Energie: Zählerstände (Alt/Neu) – Vorbelegung =====

        public sealed class EnergieZaehlerVm : INotifyPropertyChanged
        {
            private string _altText = "";
            private string _neuText = "";

            public int ZaehlerId { get; init; }
            public string Name { get; init; } = "";
            public string Typ { get; init; } = "";         // DIREKT / ALLG / HEIZ / EVU
            public int? EinheitId { get; init; }

            public string AltText
            {
                get => _altText;
                set { _altText = value ?? ""; OnPropertyChanged(); }
            }

            public string NeuText
            {
                get => _neuText;
                set { _neuText = value ?? ""; OnPropertyChanged(); }
            }

            public decimal AltKwh => ParseDecimal(AltText);
            public decimal NeuKwh => ParseDecimal(NeuText);
            public decimal DiffKwh => NeuKwh - AltKwh;

            private static decimal ParseDecimal(string? input)
            {
                // CH-tolerant: "1'234.500" / "1234,5" / "1234.5"
                var s = (input ?? "").Trim();
                s = s.Replace("’", "'").Replace(" ", "");
                s = s.Replace("'", "");      // Tausender
                s = s.Replace(",", ".");     // Dezimal

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return val;

                return 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void LoadEnergieZaehlerDefaults()
        {
            EnergieZaehler.Clear();

            // 1) Stammdaten Zähler (pro Liegenschaft)
            var zaehler = _db.StweZaehlerGetByLiegenschaft(_set.LiegenschaftId);
            if (zaehler.Count == 0)
                return; // keine Energie-Zähler definiert -> nichts zu laden

            // 2) Falls dieses Set bereits Zählerstände hat -> diese sind "Single Source of Truth"
            var existing = _db.StweEnergieZaehlerGetBySet(_set.Id);
            var existingDict = existing.ToDictionary(x => x.ZaehlerId, x => (x.AltKwh, x.NeuKwh));
            _energieHadExisting = existing.Count > 0;


            // 3) Sonst: Alt = letzter Neu-Stand vor Set-Datum
            Dictionary<int, decimal> lastNeu = new();
            if (existing.Count == 0)
            {
                lastNeu = _db.StweEnergieLastNeuStaendeGet(_set.LiegenschaftId, _set.Datum);
            }

            foreach (var z in zaehler)
            {
                // z = (Id, LiegenschaftId, Name, Typ, EinheitId, Notiz)
                var zid = z.Id;

                string alt = "";
                string neu = "";

                if (existingDict.TryGetValue(zid, out var pair))
                {
                    alt = pair.AltKwh.ToString("0.###", CultureInfo.InvariantCulture);
                    neu = pair.NeuKwh.ToString("0.###", CultureInfo.InvariantCulture);
                }
                else if (lastNeu.TryGetValue(zid, out var last))
                {
                    alt = last.ToString("0.###", CultureInfo.InvariantCulture);
                    neu = ""; // bewusst leer -> User trägt neuen Stand ein
                }

                EnergieZaehler.Add(new EnergieZaehlerVm
                {
                    ZaehlerId = zid,
                    Name = z.Name,
                    Typ = z.Typ,
                    EinheitId = z.EinheitId,
                    AltText = alt,
                    NeuText = neu
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
                // Korrektur auf die betragsmässig grösste Zeile (nach Betrag-ABS)
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

            if (!ValidateEnergieBeforeSave())
                return false;

            try
            {
                // 1) Normale Set-Verteilung (bestehend)
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

                // 2) Energie-Zählerstände (neu)
                // Nur speichern, wenn:
                // - mindestens ein Wert erfasst wurde ODER
                // - es zuvor schon Energie-Daten gab (dann muss "alles leeren" auch in DB ankommen)
                if (EnergieZaehler.Count > 0)
                {
                    var anyText = EnergieZaehler.Any(z =>
                        !string.IsNullOrWhiteSpace(z.AltText) || !string.IsNullOrWhiteSpace(z.NeuText));

                    if (anyText || _energieHadExisting)
                    {
                        var rows = EnergieZaehler
                            .Where(z => !string.IsNullOrWhiteSpace(z.AltText) || !string.IsNullOrWhiteSpace(z.NeuText))
                            .Select(z => (ZaehlerId: z.ZaehlerId, AltKwh: z.AltKwh, NeuKwh: z.NeuKwh))
                            .ToList();

                        _db.StweEnergieZaehlerReplace(_set.Id, rows);
                        _energieHadExisting = rows.Count > 0;
                    }
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
                // Belastung: Summe darf Total nicht überschreiten
                if (sum > total + eps)
                {
                    MessageBox.Show("Summe der Zeilen darf den Set-Betrag nicht überschreiten.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                // Gutschrift (total ist negativ): Summe darf nicht "mehr negativ" sein als Total
                // Beispiel: total = -100, sum = -110 -> zu viel verteilt
                if (sum < total - eps)
                {
                    MessageBox.Show("Summe der Zeilen darf den (negativen) Set-Betrag nicht unterschreiten.",
                        "Set verteilen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            return true;
        }

        private bool ValidateEnergieBeforeSave()
        {
            // Kein Energie-Block im Dialog -> ok
            if (EnergieZaehler == null || EnergieZaehler.Count == 0)
                return true;

            // Wenn der User bei einem Zähler Neu eingibt, muss Alt auch vorhanden sein.
            // Und Neu sollte nicht kleiner als Alt sein (defensive Plausibilitätsprüfung).
            foreach (var z in EnergieZaehler)
            {
                var hasAlt = !string.IsNullOrWhiteSpace(z.AltText);
                var hasNeu = !string.IsNullOrWhiteSpace(z.NeuText);

                if (hasNeu && !hasAlt)
                {
                    MessageBox.Show(
                        $"Beim Zähler „{z.Name}“ ist ein Neu-Stand gesetzt, aber Alt ist leer.\n\n" +
                        "Bitte Alt und Neu erfassen.",
                        "Energie-Zählerstände",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }

                if (hasAlt && hasNeu)
                {
                    if (z.NeuKwh < z.AltKwh)
                    {
                        MessageBox.Show(
                            $"Beim Zähler „{z.Name}“ ist Neu kleiner als Alt.\n\n" +
                            $"Alt: {z.AltKwh:0.###}  Neu: {z.NeuKwh:0.###}\n\n" +
                            "Bitte prüfen (Tippfehler?).",
                            "Energie-Zählerstände",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return false;
                    }
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
