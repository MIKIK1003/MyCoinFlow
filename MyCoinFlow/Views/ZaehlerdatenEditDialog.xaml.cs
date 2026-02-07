using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class ZaehlerdatenEditDialog : Window, INotifyPropertyChanged
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



        public sealed class RowVm : INotifyPropertyChanged
        {
            private string _neuText = "";

            public int ZaehlerId { get; init; }
            public string Typ { get; init; } = "";
            public string Name { get; init; } = "";
            public int? EinheitId { get; init; }

            public string NeuText
            {
                get => _neuText;
                set { _neuText = value ?? ""; OnPropertyChanged(); }
            }

            public decimal NeuWert => ParseDecimal(NeuText);

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
            }
            else
            {
                Model.ErfasstAm = DateTime.Today;
            }

            LoadRows();

            DataContext = this;
            OnPropertyChanged(nameof(HeaderText));
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

            // Alle Neuwerte müssen gesetzt sein (Praxistauglichkeit: immer kompletter Satz)
            if (Rows.Any(r => string.IsNullOrWhiteSpace(r.NeuText)))
            {
                MessageBox.Show("Bitte bei allen Zählern einen Neu-Wert erfassen.",
                    "Zählerdaten", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
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
