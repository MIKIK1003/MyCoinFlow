using MyCoinFlow.Models;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class VermoegenApiEinstellungDialog
    {
        public VermoegenApiEinstellung Model { get; }

        public VermoegenApiEinstellungDialog(VermoegenApiEinstellung model)
        {
            InitializeComponent();

            Model = new VermoegenApiEinstellung
            {
                Id = model.Id,
                ApiProvider = string.IsNullOrWhiteSpace(model.ApiProvider) ? "EODHD" : model.ApiProvider,
                ApiKey = model.ApiKey ?? "",
                Aktiv = model.Aktiv
            };

            DataContext = this;

            Loaded += (_, _) =>
            {
                ApiKeyBox.Focus();
                ApiKeyBox.SelectAll();
            };
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            Model.ApiProvider = "EODHD";
            Model.ApiKey = (Model.ApiKey ?? "").Trim();

            DialogResult = true;
        }
    }
}