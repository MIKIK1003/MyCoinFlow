using System.Windows;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class EigentuemerNeuDialog : Window
    {
        public StweEigentuemer Model { get; } = new();

        public EigentuemerNeuDialog()
        {
            InitializeComponent();
            DataContext = Model;

            Loaded += (_, __) => { try { NameBox?.Focus(); } catch { } };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte Name eingeben.", "Eigentümer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                try { NameBox?.Focus(); } catch { }
                return;
            }
            DialogResult = true;
        }
    }
}
