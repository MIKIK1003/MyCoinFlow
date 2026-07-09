using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class VermoegenKursHistorieWindow
    {
        private readonly DatabaseService _db = new();
        private readonly VermoegenPosition _position;

        private static readonly CultureInfo ChCulture = CultureInfo.GetCultureInfo("de-CH");

        public string TitelText { get; }
        public string UntertitelText { get; }

        public ObservableCollection<KursHistorieRow> Historie { get; } = new();

        private KursHistorieRow? _selectedHistorie;
        public KursHistorieRow? SelectedHistorie
        {
            get => _selectedHistorie;
            set
            {
                _selectedHistorie = value;
                if (value != null)
                {
                    EingabeDatum = value.KursDatum;
                    EingabeKursText = value.Kurs.ToString("N6", ChCulture).TrimEnd('0').TrimEnd('.');
                    StatusText = $"Eintrag vom {value.KursDatumText} ausgewählt.";
                    RefreshBindings();
                }
            }
        }

        private ISeries[] _kursSeries = Array.Empty<ISeries>();
        public ISeries[] KursSeries
        {
            get => _kursSeries;
            set { _kursSeries = value; }
        }

        private Axis[] _xAxes = Array.Empty<Axis>();
        public Axis[] XAxes
        {
            get => _xAxes;
            set { _xAxes = value; }
        }

        private Axis[] _yAxes = Array.Empty<Axis>();
        public Axis[] YAxes
        {
            get => _yAxes;
            set { _yAxes = value; }
        }

        public DateTime? EingabeDatum { get; set; } = DateTime.Today;
        public string EingabeKursText { get; set; } = "";

        public string StatusText { get; set; } = "Bereit.";

        public VermoegenKursHistorieWindow(VermoegenPosition position)
        {
            InitializeComponent();

            _position = position;

            TitelText = $"Kursverlauf – {position.Titel}";
            UntertitelText = BuildUntertitel(position);

            Reload();

            DataContext = this;
        }

        private void Reload()
        {
            Historie.Clear();

            var daten = _db.VermoegenKursHistorieGetByPosition(_position.Id)
                .OrderBy(h => h.KursDatum)
                .ToList();

            foreach (var h in daten.OrderByDescending(h => h.KursDatum))
            {
                Historie.Add(new KursHistorieRow
                {
                    Id = h.Id,
                    PositionId = h.PositionId,
                    KursDatum = h.KursDatum,
                    Kurs = h.Kurs,
                    KursDatumText = h.KursDatum.ToString("dd.MM.yyyy"),
                    KursText = h.Kurs.ToString("N2", ChCulture),
                    Quelle = h.Quelle,
                    ErfasstAmText = h.ErfasstAm.ToString("dd.MM.yyyy HH:mm")
                });
            }

            KursSeries = new ISeries[]
            {
                new LineSeries<decimal>
                {
                    Values = daten.Select(h => h.Kurs).ToArray(),
                    Name = "Kurs",
                    GeometrySize = 8,
                    Fill = null
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = daten.Select(h => h.KursDatum.ToString("dd.MM.yy")).ToArray(),
                    LabelsRotation = 35
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Labeler = value => value.ToString("N2", ChCulture)
                }
            };

            RefreshBindings();
        }

        private void KursSpeichern_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadInput(out var kursDatum, out var kurs))
                return;

            var bereitsVorhanden = _db.VermoegenKursHistorieGetByPosition(_position.Id)
                .Any(h => h.KursDatum.Date == kursDatum.Date);

            if (bereitsVorhanden)
            {
                MessageBox.Show(
                    "Für dieses Datum ist bereits ein Kurs vorhanden. Es wird kein Duplikat gespeichert.",
                    "Kurs speichern",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText = "Kursdatum bereits vorhanden.";
                RefreshBindings();
                return;
            }

            _db.VermoegenKursHistorieInsertIfMissing(
                _position.Id,
                kursDatum.Date,
                kurs,
                "Manuell");

            StatusText = "Manueller Kurs gespeichert.";
            EingabeKursText = "";
            SelectedHistorie = null;

            Reload();
        }

        private void KursAktualisieren_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedHistorie == null)
            {
                MessageBox.Show(
                    "Bitte zuerst einen Kurseintrag auswählen.",
                    "Kurs aktualisieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!TryReadInput(out var kursDatum, out var kurs))
                return;

            var datumBereitsAndererEintrag = _db.VermoegenKursHistorieGetByPosition(_position.Id)
                .Any(h => h.Id != SelectedHistorie.Id && h.KursDatum.Date == kursDatum.Date);

            if (datumBereitsAndererEintrag)
            {
                MessageBox.Show(
                    "Für dieses Datum existiert bereits ein anderer Kurseintrag.",
                    "Kurs aktualisieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _db.VermoegenKursHistorieUpdate(
                SelectedHistorie.Id,
                kursDatum.Date,
                kurs,
                "Manuell");

            StatusText = "Kurseintrag aktualisiert.";
            SelectedHistorie = null;
            EingabeKursText = "";

            Reload();
        }

        private void KursLoeschen_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedHistorie == null)
            {
                MessageBox.Show(
                    "Bitte zuerst einen Kurseintrag auswählen.",
                    "Kurs löschen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show(
                $"Kurseintrag wirklich löschen?\n\n{SelectedHistorie.KursDatumText} · {SelectedHistorie.KursText} · {SelectedHistorie.Quelle}",
                "Kurs löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            _db.VermoegenKursHistorieDelete(SelectedHistorie.Id);

            StatusText = "Kurseintrag gelöscht.";
            SelectedHistorie = null;
            EingabeKursText = "";

            Reload();
        }

        private bool TryReadInput(out DateTime kursDatum, out decimal kurs)
        {
            kursDatum = DateTime.Today;
            kurs = 0;

            if (!EingabeDatum.HasValue)
            {
                MessageBox.Show(
                    "Bitte ein Kursdatum erfassen.",
                    "Kurs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            if (!TryParseDecimal(EingabeKursText, out kurs) || kurs <= 0)
            {
                MessageBox.Show(
                    "Bitte einen gültigen Kurs größer 0 erfassen.",
                    "Kurs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            kursDatum = EingabeDatum.Value.Date;
            return true;
        }

        private static string BuildUntertitel(VermoegenPosition position)
        {
            var parts = new[]
            {
                string.IsNullOrWhiteSpace(position.ISIN) ? null : $"ISIN: {position.ISIN}",
                string.IsNullOrWhiteSpace(position.Valor) ? null : $"Valor: {position.Valor}",
                string.IsNullOrWhiteSpace(position.Symbol) ? null : $"Symbol: {position.Symbol}",
                string.IsNullOrWhiteSpace(position.Boerse) ? null : $"Börse: {position.Boerse}",
                string.IsNullOrWhiteSpace(position.Waehrung) ? null : $"Währung: {position.Waehrung}"
            };

            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static bool TryParseDecimal(string? text, out decimal value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleaned = text.Trim()
                .Replace("'", "")
                .Replace(" ", "");

            return decimal.TryParse(
                cleaned,
                NumberStyles.Number,
                ChCulture,
                out value);
        }

        private void RefreshBindings()
        {
            DataContext = null;
            DataContext = this;
        }
    }

    public class KursHistorieRow
    {
        public int Id { get; set; }
        public int PositionId { get; set; }

        public DateTime KursDatum { get; set; }
        public decimal Kurs { get; set; }

        public string KursDatumText { get; set; } = "";
        public string KursText { get; set; } = "";
        public string Quelle { get; set; } = "";
        public string ErfasstAmText { get; set; } = "";
    }
}