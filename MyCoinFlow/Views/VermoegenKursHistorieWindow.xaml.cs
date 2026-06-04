using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace MyCoinFlow.Views
{
    public partial class VermoegenKursHistorieWindow
    {
        private readonly DatabaseService _db = new();
        private static readonly CultureInfo ChCulture = CultureInfo.GetCultureInfo("de-CH");

        public string TitelText { get; }
        public string UntertitelText { get; }

        public ObservableCollection<KursHistorieRow> Historie { get; } = new();

        public ISeries[] KursSeries { get; }
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        public VermoegenKursHistorieWindow(VermoegenPosition position)
        {
            InitializeComponent();

            TitelText = $"Kursverlauf – {position.Titel}";
            UntertitelText = BuildUntertitel(position);

            var daten = _db.VermoegenKursHistorieGetByPosition(position.Id)
                .OrderBy(h => h.KursDatum)
                .ToList();

            foreach (var h in daten.OrderByDescending(h => h.KursDatum))
            {
                Historie.Add(new KursHistorieRow
                {
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

            DataContext = this;
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
    }

    public class KursHistorieRow
    {
        public string KursDatumText { get; set; } = "";
        public string KursText { get; set; } = "";
        public string Quelle { get; set; } = "";
        public string ErfasstAmText { get; set; } = "";
    }
}