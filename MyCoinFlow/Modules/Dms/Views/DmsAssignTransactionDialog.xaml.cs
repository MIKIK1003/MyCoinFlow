using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Zeigt Kandidaten-Transaktionen für ein DMS-Dokument an und lässt den User eine davon
    /// wählen (Mehrdeutigkeits-Fall beim automatischen Matching) oder frei danach suchen
    /// (manuelles Zuweisen eines bereits archivierten, noch nicht verknüpften Dokuments).
    /// </summary>
    public partial class DmsAssignTransactionDialog : BaseWindow
    {
        private readonly DatabaseService _db = new();

        public int? AusgewaehlteTransaktionId { get; private set; }

        private sealed class Row
        {
            public int Id { get; set; }
            public string DatumAnzeige { get; set; } = "";
            public string BetragAnzeige { get; set; } = "";
            public string WerAnzeige { get; set; } = "";
            public string? Notiz { get; set; }
        }

        /// <param name="vorschlaege">
        /// Vorgefilterte Kandidaten (Mehrdeutigkeits-Fall). Null/leer -> manueller Suchmodus.
        /// </param>
        public DmsAssignTransactionDialog(List<Transaktion>? vorschlaege = null)
        {
            InitializeComponent();

            if (vorschlaege != null && vorschlaege.Count > 0)
            {
                HinweisText.Text = $"{vorschlaege.Count} passende Transaktionen gefunden – bitte auswählen oder \"Keine Zuordnung\".";
                Fill(vorschlaege);
            }
            else
            {
                HinweisText.Text = "Kein automatischer Treffer. Bitte Suchkriterien angeben und suchen.";
            }
        }

        private void SuchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Suchen_Click(sender, e);
        }

        private void AuchVerknuepfte_Changed(object sender, RoutedEventArgs e)
        {
            // Umschalten wirkt direkt auf die aktuelle Suche
            if (IsLoaded) Suchen_Click(sender, e);
        }

        private void Suchen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                decimal? betrag = decimal.TryParse(BetragBox.Text?.Trim(), NumberStyles.Number,
                    CultureInfo.CurrentCulture, out var b) ? b : null;

                // Bereits mit einem Dokument verknüpfte Transaktionen standardmässig ausblenden
                // (ein Rechnungsdokument gehört zu genau einer Zahlung). Ausnahme per Checkbox:
                // Sammelbuchungen, denen mehrere Rechnungen zugeordnet werden müssen.
                var auchVerknuepfte = AuchVerknuepfteCheck.IsChecked == true;

                var result = _db.SearchTransaktionenForZuordnung(
                    SuchTextBox.Text, betrag, VonPicker.SelectedDate, BisPicker.SelectedDate,
                    nurOhneDokument: !auchVerknuepfte);
                Fill(result);

                if (result.Count == 0)
                    HinweisText.Text = auchVerknuepfte
                        ? "Keine Treffer."
                        : "Keine Treffer. Hinweis: Transaktionen mit bereits verknüpftem Dokument werden ausgeblendet (Checkbox unten einschalten, um sie zu sehen).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Suche fehlgeschlagen: " + ex.Message, "Transaktion zuweisen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Fill(List<Transaktion> transaktionen)
        {
            GridKandidaten.ItemsSource = transaktionen.Select(t => new Row
            {
                Id = t.Id,
                DatumAnzeige = t.Datum.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                BetragAnzeige = t.Betrag.ToString("N2", CultureInfo.CurrentCulture),
                WerAnzeige = t.AdresseName ?? t.BankName ?? "–",
                Notiz = t.Notiz
            }).ToList();
        }

        private void Zuweisen_Click(object sender, RoutedEventArgs e)
        {
            if (GridKandidaten.SelectedItem is not Row row)
            {
                MessageBox.Show(this, "Bitte zuerst eine Transaktion auswählen.", "Transaktion zuweisen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AusgewaehlteTransaktionId = row.Id;
            DialogResult = true;
        }

        private void KeineZuordnung_Click(object sender, RoutedEventArgs e)
        {
            AusgewaehlteTransaktionId = null;
            DialogResult = false;
        }
    }
}
