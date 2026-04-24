using MyCoinFlow.Models;
using MyCoinFlow.UI.Base; // NEU
using System.Windows;
using MessageBox = System.Windows.MessageBox; // Fix Mehrdeutigkeit

namespace MyCoinFlow.Views
{
    public partial class LiegenschaftNeuDialog : BaseWindow // NEU
    {
        public StweLiegenschaft Model { get; } = new();

        public LiegenschaftNeuDialog()
        {
            InitializeComponent();
            DataContext = Model;

            Loaded += (_, __) =>
            {
                try { NameBox?.Focus(); } catch { }
            };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte einen Namen eingeben.", "Liegenschaft",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                try { NameBox?.Focus(); } catch { }
                return;
            }

            DialogResult = true;
        }
    }
}