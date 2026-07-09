using System.Windows;
using Microsoft.Win32;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;

namespace MyCoinFlow.Views
{
    public partial class DmsDocumentDialog : BaseWindow
    {
        private readonly bool _istNeu;

        public string? Titel { get; private set; }
        public string? Kategorie { get; private set; }
        public string? AusgewaehlteDateiPfad { get; private set; }

        public DmsDocumentDialog(DmsDocument? bestehend = null)
        {
            InitializeComponent();

            _istNeu = bestehend == null;
            Title = _istNeu ? "Dokument hochladen" : "Dokument bearbeiten";
            HeaderText.Text = Title;

            DateiPanel.Visibility = _istNeu ? Visibility.Visible : Visibility.Collapsed;

            var db = new DatabaseService();
            KategorieBox.ItemsSource = db.GetDistinctKategorien();

            if (bestehend != null)
            {
                TitelBox.Text = bestehend.Titel ?? "";
                KategorieBox.Text = bestehend.Kategorie ?? "";
            }
        }

        private void DateiWaehlen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Dokument auswählen",
                Filter = "Dokumente (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                AusgewaehlteDateiPfad = dlg.FileName;
                DateiPfadBox.Text = dlg.FileName;
            }
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (_istNeu && string.IsNullOrWhiteSpace(AusgewaehlteDateiPfad))
            {
                MessageBox.Show(this, "Bitte zuerst eine Datei auswählen.", "Datei fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Titel = string.IsNullOrWhiteSpace(TitelBox.Text) ? null : TitelBox.Text.Trim();
            Kategorie = string.IsNullOrWhiteSpace(KategorieBox.Text) ? null : KategorieBox.Text.Trim();

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
