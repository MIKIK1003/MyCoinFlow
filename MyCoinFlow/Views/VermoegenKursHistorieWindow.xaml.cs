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

        public VermoegenKursHistorieWindow(VermoegenPosition position)
        {
            InitializeComponent();

            TitelText = $"Kursverlauf – {position.Titel}";
            UntertitelText = BuildUntertitel(position);

            foreach (var h in _db.VermoegenKursHistorieGetByPosition(position.Id))
            {
                Historie.Add(new KursHistorieRow
                {
                    KursDatumText = h.KursDatum.ToString("dd.MM.yyyy"),
                    KursText = h.Kurs.ToString("N2", ChCulture),
                    Quelle = h.Quelle,
                    ErfasstAmText = h.ErfasstAm.ToString("dd.MM.yyyy HH:mm")
                });
            }

            DataContext = this;
        }

        private static string BuildUntertitel(VermoegenPosition position)
        {
            var parts = new[]
            {
                string.IsNullOrWhiteSpace(position.ISIN) ? null : $"ISIN: {position.ISIN}",
                string.IsNullOrWhiteSpace(position.Valor) ? null : $"Valor: {position.Valor}",
                string.IsNullOrWhiteSpace(position.Symbol) ? null : $"Symbol: {position.Symbol}",
                string.IsNullOrWhiteSpace(position.Boerse) ? null : $"Börse: {position.Boerse}"
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