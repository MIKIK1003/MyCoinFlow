using System.Globalization;
using MyCoinFlow.Models;
using MyCoinFlow.UI.Base; // NEU
using System.Windows;
using MessageBox = System.Windows.MessageBox; // Fix Mehrdeutigkeit

namespace MyCoinFlow.Views
{
    public partial class EinheitNeuDialog : BaseWindow // NEU
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

            // Zahlenfelder bewusst tolerant (kein Parsing hier erzwungen)
            DialogResult = true;
        }
    }
}