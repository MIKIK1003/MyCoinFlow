using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.Views
{
    public partial class AboTransaktionZuordnenDialog
    {
        private readonly DatabaseService _db = new();

        public List<int> AusgewaehlteTransaktionIds { get; } = new();

        public AboTransaktionZuordnenDialog(string? vorbelegterSuchtext = null)
        {
            InitializeComponent();

            // Sinnvoller Default: letzte 2 Jahre
            VonPicker.SelectedDate = DateTime.Today.AddYears(-2);
            BisPicker.SelectedDate = DateTime.Today;

            // Mit Anbietername vorbefüllen und direkt suchen (spart Klicks)
            if (!string.IsNullOrWhiteSpace(vorbelegterSuchtext))
            {
                SuchTextBox.Text = vorbelegterSuchtext.Trim();
                Loaded += (_, _) => Suchen();
            }
        }

        private void Suchen_Click(object sender, RoutedEventArgs e) => Suchen();

        private void SuchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Suchen();
        }

        private void Suchen()
        {
            try
            {
                var text = string.IsNullOrWhiteSpace(SuchTextBox.Text) ? null : SuchTextBox.Text.Trim();

                decimal? betrag = null;
                if (!string.IsNullOrWhiteSpace(BetragBox.Text))
                {
                    var t = BetragBox.Text.Trim().Replace("'", "");
                    if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out var v)
                        || decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out v))
                        betrag = v;
                }

                var treffer = _db.SearchTransaktionenForZuordnung(
                    text, betrag, VonPicker.SelectedDate, BisPicker.SelectedDate, maxResults: 200);

                ErgebnisGrid.ItemsSource = treffer;
                HinweisText.Text = treffer.Count == 0
                    ? "Keine Treffer"
                    : $"{treffer.Count} Treffer – Mehrfachauswahl mit Ctrl/Shift möglich";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Suche fehlgeschlagen:\n" + ex.Message,
                    "Transaktion zuordnen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ErgebnisGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ErgebnisGrid.SelectedItem is Transaktion)
                Zuordnen_Click(sender, e);
        }

        private void Zuordnen_Click(object sender, RoutedEventArgs e)
        {
            var auswahl = ErgebnisGrid.SelectedItems.OfType<Transaktion>().ToList();

            if (auswahl.Count == 0)
            {
                MessageBox.Show("Bitte mindestens eine Transaktion auswählen.",
                    "Transaktion zuordnen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AusgewaehlteTransaktionIds.Clear();
            AusgewaehlteTransaktionIds.AddRange(auswahl.Select(t => t.Id));

            DialogResult = true;
        }
    }
}
