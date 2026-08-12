using System.Windows;
using Microsoft.Win32;
using MyCoinFlow.UI.Base;

namespace MyCoinFlow.Views
{
    public partial class DmsNewVersionDialog : BaseWindow
    {
        public string? SelectedFilePath { get; private set; }
        public string? Comment { get; private set; }

        public DmsNewVersionDialog() => InitializeComponent();

        private void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Neue Dokumentversion auswählen",
                Filter = "Dokumente (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            SelectedFilePath = dialog.FileName;
            FilePathBox.Text = dialog.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath))
            {
                MessageBox.Show(this, "Bitte zuerst eine Datei auswählen.", "Datei fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
