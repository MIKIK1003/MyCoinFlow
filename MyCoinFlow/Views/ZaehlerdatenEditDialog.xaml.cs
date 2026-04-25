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
    public partial class ZaehlerdatenEditDialog : BaseWindow, INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public StweZaehlerdatenSet Model { get; } = new();

        public string HeaderText => Model.Id > 0 ? "Zählerdaten bearbeiten" : "Zählerdaten neu";

        public DateTime? ModelErfasstAm
        {
            get => Model.ErfasstAm == default ? (DateTime?)null : Model.ErfasstAm;
            set { Model.ErfasstAm = value ?? default; OnPropertyChanged(); }
        }

        public string RechnungKwhText
        {
            get => Model.RechnungKwhTotal?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
            set
            {
                Model.RechnungKwhTotal = ParseDecimalOrNull(value);
                OnPropertyChanged();
            }
        }

        public string GutschriftChfText
        {
            get => Model.GutschriftChf?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            set
            {
                Model.GutschriftChf = ParseDecimalOrNull(value);
                OnPropertyChanged();
            }
        }

        public string RueckgespeistKwhText
        {
            get => Model.RueckgespeistKwh?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
            set
            {
                Model.RueckgespeistKwh = ParseDecimalOrNull(value);
                OnPropertyChanged();
            }
        }

        public int ErfassungsTypProxy
        {
            get => Model.ErfassungsTyp;
            set
            {
                if (Model.ErfassungsTyp == value)
                    return;

                Model.ErfassungsTyp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMonatswerte));
                OnPropertyChanged(nameof(MonatswerteVisibility));
            }
        }

        public int? MonatsAnzahlProxy
        {
            get => Model.MonatsAnzahl;
            set
            {
                if (Model.MonatsAnzahl == value)
                    return;

                Model.MonatsAnzahl = value;
                EnsureRowMonthSlots();
                OnPropertyChanged();
            }
        }

        public bool IsMonatswerte
        {
            get => Model.ErfassungsTyp == 1;
        }

        public Visibility MonatswerteVisibility
        {
            get => IsMonatswerte ? Visibility.Visible : Visibility.Collapsed;
        }

        public sealed class MonatVm : INotifyPropertyChanged
        {
            private string _text = "";

            public int MonatIndex { get; init; }

            public string Text
            {
                get => _text;
                set
                {
                    if (_text == value)
                        return;

                    _text = value ?? "";
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Kwh));
                }
            }

            public decimal Kwh => ParseDecimal(Text);

            private static decimal ParseDecimal(string? input)
            {
                var s = (input ?? "").Trim().Replace("’", "'").Replace(" ", "").Replace("'", "").Replace(",", ".");
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return val;
                return 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public sealed class RowVm : INotifyPropertyChanged
        {
            private string _neuText = "";

            public int ZaehlerId { get; init; }
            public string Typ { get; init; } = "";
            public string Name { get; init; } = "";
            public int? EinheitId { get; init; }

            // Bestehender Modus: absoluter Zählerstand
            public string NeuText
            {
                get => _neuText;
                set
                {
                    _neuText = value ?? "";
                    OnPropertyChanged();
                }
            }

            public decimal NeuWert => ParseDecimal(NeuText);

            // Neuer Modus: Monatswerte (kWh)
            public ObservableCollection<MonatVm> Monatswerte { get; } = new();

            public RowVm()
            {
                Monatswerte.CollectionChanged += Monatswerte_CollectionChanged;
            }

            public decimal MonatswerteSumme => Monatswerte.Sum(x => x.Kwh);

            public string MonatswerteInfo
            {
                get
                {
                    if (Monatswerte.Count == 0)
                        return "keine Monatswerte";

                    return $"{Monatswerte.Count} Monat(e), Summe {MonatswerteSumme:0.###} kWh";
                }
            }

            public void EnsureMonthSlots(int count)
            {
                if (count < 0) count = 0;

                while (Monatswerte.Count < count)
                {
                    var item = new MonatVm { MonatIndex = Monatswerte.Count + 1, Text = "" };
                    item.PropertyChanged += MonatItem_PropertyChanged;
                    Monatswerte.Add(item);
                }

                while (Monatswerte.Count > count)
                {
                    var last = Monatswerte[Monatswerte.Count - 1];
                    last.PropertyChanged -= MonatItem_PropertyChanged;
                    Monatswerte.RemoveAt(Monatswerte.Count - 1);
                }

                RefreshMonatswerteDerivedProperties();
            }

            public bool AreAllMonthValuesFilled()
            {
                if (Monatswerte.Count == 0)
                    return false;

                return Monatswerte.All(x => !string.IsNullOrWhiteSpace(x.Text));
            }

            public List<(int MonatIndex, decimal Kwh)> GetMonthValues()
            {
                var list = new List<(int MonatIndex, decimal Kwh)>();

                foreach (var item in Monatswerte.OrderBy(x => x.MonatIndex))
                    list.Add((item.MonatIndex, item.Kwh));

                return list;
            }

            private void Monatswerte_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                RefreshMonatswerteDerivedProperties();
            }

            private void MonatItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(MonatVm.Text) || e.PropertyName == nameof(MonatVm.Kwh))
                    RefreshMonatswerteDerivedProperties();
            }

            private void RefreshMonatswerteDerivedProperties()
            {
                OnPropertyChanged(nameof(Monatswerte));
                OnPropertyChanged(nameof(MonatswerteSumme));
                OnPropertyChanged(nameof(MonatswerteInfo));
            }

            private static decimal ParseDecimal(string? input)
            {
                var s = (input ?? "").Trim().Replace("’", "'").Replace(" ", "").Replace("'", "").Replace(",", ".");
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return val;
                return 0m;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<RowVm> Rows { get; } = new();

        public ZaehlerdatenEditDialog(int liegenschaftId, StweZaehlerdatenSet? existing = null)
        {
            InitializeComponent();

            Model.LiegenschaftId = liegenschaftId;

            if (existing != null)
            {
                Model.Id = existing.Id;
                Model.LiegenschaftId = existing.LiegenschaftId;
                Model.ErfasstAm = existing.ErfasstAm;
                Model.RechnungKwhTotal = existing.RechnungKwhTotal;
                Model.GutschriftChf = existing.GutschriftChf;
                Model.RueckgespeistKwh = existing.RueckgespeistKwh;
                Model.Notiz = existing.Notiz;
                Model.ErfassungsTyp = existing.ErfassungsTyp;
                Model.MonatsAnzahl = existing.MonatsAnzahl;
            }
            else
            {
                Model.ErfasstAm = DateTime.Today;
                Model.ErfassungsTyp = 0;
                Model.MonatsAnzahl = null;
            }

            LoadRows();
            EnsureRowMonthSlots();
            LoadExistingMonthValues();

            DataContext = this;
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(ErfassungsTypProxy));
            OnPropertyChanged(nameof(MonatsAnzahlProxy));
            OnPropertyChanged(nameof(IsMonatswerte));
            OnPropertyChanged(nameof(MonatswerteVisibility));
        }

        private void LoadRows()
        {
            Rows.Clear();

            var zaehler = _db.StweZaehlerGetByLiegenschaft(Model.LiegenschaftId);

            // Vorbelegung aus bestehendem Set (Bearbeiten)
            Dictionary<int, decimal> existingNeu = new();
            if (Model.Id > 0)
            {
                var lines = _db.StweZaehlerdatenLinesGetBySet(Model.Id);
                existingNeu = lines.ToDictionary(x => x.ZaehlerId, x => x.NeuWert);
            }
            else
            {
                // Vorbelegung: letzte Werte (neues Set) – nimmt letzten NeuWert pro Zähler aus letztem Set
                var last = _db.StweZaehlerdatenSetsGetByLiegenschaft(Model.LiegenschaftId).FirstOrDefault();
                if (last != null)
                {
                    var lines = _db.StweZaehlerdatenLinesGetBySet(last.Id);
                    existingNeu = lines.ToDictionary(x => x.ZaehlerId, x => x.NeuWert);
                }
            }

            foreach (var z in zaehler)
            {
                existingNeu.TryGetValue(z.Id, out var nw);

                Rows.Add(new RowVm
                {
                    ZaehlerId = z.Id,
                    Typ = z.Typ,
                    Name = z.Name,
                    EinheitId = z.EinheitId,
                    NeuText = nw > 0m ? nw.ToString("0.###", CultureInfo.InvariantCulture) : ""
                });
            }
        }

        private void EnsureRowMonthSlots()
        {
            var count = Model.MonatsAnzahl ?? 0;
            if (count < 0) count = 0;

            foreach (var row in Rows)
                row.EnsureMonthSlots(count);
        }

        private void LoadExistingMonthValues()
        {
            if (Model.Id <= 0)
                return;

            if (Model.ErfassungsTyp != 1)
                return;

            var monate = _db.StweZaehlerdatenMonateGetBySet(Model.Id);
            if (monate == null || monate.Count == 0)
                return;

            foreach (var row in Rows)
            {
                var rowMonate = monate
                    .Where(x => x.ZaehlerId == row.ZaehlerId)
                    .OrderBy(x => x.MonatIndex)
                    .ToList();

                if (rowMonate.Count == 0)
                    continue;

                var maxIndex = rowMonate.Max(x => x.MonatIndex);
                row.EnsureMonthSlots(maxIndex);

                foreach (var m in rowMonate)
                {
                    var slot = row.Monatswerte.FirstOrDefault(x => x.MonatIndex == m.MonatIndex);
                    if (slot != null)
                        slot.Text = m.Kwh.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
                return;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private bool Validate()
        {
            if (Model.LiegenschaftId <= 0)
            {
                MessageBox.Show("Liegenschaft fehlt.", "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Model.ErfasstAm == default)
            {
                MessageBox.Show("Bitte ein Erfassungsdatum wählen.", "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.Count == 0)
            {
                MessageBox.Show("Keine Zähler vorhanden. Bitte zuerst Zähler unter „Liegenschaften → Zähler“ erfassen.",
                    "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Model.ErfassungsTyp == 1)
            {
                if (!Model.MonatsAnzahl.HasValue || Model.MonatsAnzahl.Value <= 0)
                {
                    MessageBox.Show("Bitte Anzahl Monate > 0 erfassen.",
                        "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                if (Rows.Any(r => !r.AreAllMonthValuesFilled()))
                {
                    MessageBox.Show("Bitte bei allen Zählern alle Monatswerte erfassen.",
                        "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                // Bestehender Modus: absoluter Neu-Wert pro Zähler
                if (Rows.Any(r => string.IsNullOrWhiteSpace(r.NeuText)))
                {
                    MessageBox.Show("Bitte bei allen Zählern einen Neu-Wert erfassen.",
                        "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            return true;
        }

        private decimal? ParseDecimalOrNull(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var s = text.Trim();

            // CH / EU tolerant:
            // 1'234.50
            // 1’234.50
            // 1234,50
            // 1234.50
            s = s.Replace("’", "'")
                 .Replace("'", "")
                 .Replace(" ", "")
                 .Replace(",", ".");

            if (decimal.TryParse(
                s,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
            {
                return value;
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}