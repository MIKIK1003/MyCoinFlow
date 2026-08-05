using MyCoinFlow.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Auswahl eines freien (unverknüpften) DMS-Dokuments, um es an eine
    /// Transaktion anzuhängen. Alternativ kann der Benutzer auf die klassische
    /// Explorer-Auswahl ausweichen (ExplorerGewaehlt = true).
    /// </summary>
    public partial class DmsDokumentWahlDialog
    {
        private readonly DatabaseService _db = new();

        /// <summary>Id des gewählten DMS-Dokuments (0 = keines gewählt).</summary>
        public int AusgewaehltesDokumentId { get; private set; }

        /// <summary>True, wenn der Benutzer stattdessen die Explorer-Auswahl möchte.</summary>
        public bool ExplorerGewaehlt { get; private set; }

        public DmsDokumentWahlDialog()
        {
            InitializeComponent();
            Laden();
        }

        private void Laden()
        {
            try
            {
                var freie = _db.LoadAllDocuments(SuchTextBox.Text, null)
                    .Where(d => d.EntityType == null)
                    .ToList();

                DokumenteGrid.ItemsSource = freie;

                StatusText.Text = freie.Count == 0
                    ? "Keine freien Dokumente gefunden – ggf. über «Datei aus Explorer…» anhängen."
                    : $"{freie.Count} freie Dokumente (ohne Transaktions-Verknüpfung).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Dokumente konnten nicht geladen werden:\n" + ex.Message,
                    "Dokument aus DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SuchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Laden();
        }

        private void Suchen_Click(object sender, RoutedEventArgs e) => Laden();

        private void DokumenteGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DokumenteGrid.SelectedItem != null)
                Anhaengen_Click(sender, e);
        }

        private void Anhaengen_Click(object sender, RoutedEventArgs e)
        {
            if (DokumenteGrid.SelectedItem is not MyCoinFlow.Models.DmsDocument doc)
            {
                MessageBox.Show(this, "Bitte zuerst ein Dokument auswählen.",
                    "Dokument aus DMS", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AusgewaehltesDokumentId = doc.Id;
            DialogResult = true;
        }

        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            ExplorerGewaehlt = true;
            DialogResult = true;
        }
    }
}
