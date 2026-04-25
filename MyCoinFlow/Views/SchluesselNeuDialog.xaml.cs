using System;
using System.Windows;
using MyCoinFlow.Models;
using MyCoinFlow.UI.Base; // NEU
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    public partial class SchluesselNeuDialog : BaseWindow // NEU
    {
        public StweSchluessel Model { get; } = new();

        public SchluesselNeuDialog(int liegenschaftId)
        {
            InitializeComponent();

            Model.LiegenschaftId = liegenschaftId;
            Model.Modus = "FIX";

            DataContext = Model;

            Loaded += (_, __) => { try { NameBox?.Focus(); } catch { } };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            Model.Name = (NameBox?.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte einen Namen erfassen.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Model.Modus = (Model.Modus ?? "").Trim().ToUpperInvariant();
            if (Model.Modus != "FIX" && Model.Modus != "MEA" && Model.Modus != "ENERGIE")
                Model.Modus = "FIX";

            DialogResult = true;
        }
    }
}