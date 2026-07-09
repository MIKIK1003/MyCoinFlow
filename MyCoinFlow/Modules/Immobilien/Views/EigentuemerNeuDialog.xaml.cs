using MyCoinFlow.Models;
using MyCoinFlow.UI.Base; // NEU
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    public partial class EigentuemerNeuDialog : BaseWindow // NEU
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