using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class SchluesselNeuDialog : Window
    {
        public StweSchluessel Model { get; } = new();

        public SchluesselNeuDialog()
        {
            InitializeComponent();
            Model.Modus = "FIX";
            DataContext = Model;

            Loaded += (_, __) => { try { NameBox?.Focus(); } catch { } };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte Name eingeben.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Model.Modus != "FIX" && Model.Modus != "MEA")
                Model.Modus = "FIX";

            DialogResult = true;
        }
    }
}
