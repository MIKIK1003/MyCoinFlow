using System.Globalization;
using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class EinheitNeuDialog : Window
    {
        public StweEinheit Model { get; } = new();

        public EinheitNeuDialog(int liegenschaftId)
        {
            InitializeComponent();

            Model.LiegenschaftId = liegenschaftId;
            DataContext = Model;

            Loaded += (_, __) =>
            {
                try { BezBox?.Focus(); } catch { }
            };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Model.Bezeichnung))
            {
                MessageBox.Show("Bitte eine Bezeichnung eingeben.", "Einheit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                try { BezBox?.Focus(); } catch { }
                return;
            }

            // Zahlenfelder tolerant: Nutzer kann "12.5" oder "12,5" tippen
            // (Optional: später schöner mit NumericUpDown, aber kein Gefrickel jetzt)
            DialogResult = true;
        }
    }
}
