using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.Views
{
    public partial class AboKontoWahlDialog
    {
        /// <summary>Ein im Abo verwendetes Buchungskonto samt Verteilung.</summary>
        public class KontoOption
        {
            public int KontoId { get; set; }
            public string Anzeige { get; set; } = "";
            public int Anzahl { get; set; }
            public DateTime? LetzteZahlung { get; set; }
        }

        public int? GewaehltesKontoId { get; private set; }

        public AboKontoWahlDialog(List<KontoOption> optionen, int? vorauswahlKontoId, string aboName)
        {
            InitializeComponent();

            Title = $"Zielkonto wählen – {aboName}";
            KontenGrid.ItemsSource = optionen;

            var vorauswahl = optionen.FirstOrDefault(o => o.KontoId == vorauswahlKontoId);
            if (vorauswahl != null)
                KontenGrid.SelectedItem = vorauswahl;
        }

        private void KontenGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (KontenGrid.SelectedItem is KontoOption)
                Uebernehmen_Click(sender, e);
        }

        private void Uebernehmen_Click(object sender, RoutedEventArgs e)
        {
            if (KontenGrid.SelectedItem is not KontoOption option)
            {
                MessageBox.Show("Bitte ein Konto auswählen.",
                    "Zielkonto wählen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GewaehltesKontoId = option.KontoId;
            DialogResult = true;
        }
    }
}
