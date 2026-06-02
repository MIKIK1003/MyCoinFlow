using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class VermoegenDepotDialog
    {
        private readonly DatabaseService _db = new();

        public VermoegenDepot Model { get; }

        public ObservableCollection<string> Waehrungen { get; } = new()
        {
            "CHF",
            "EUR",
            "USD",
            "GBP"
        };

        public ObservableCollection<VermoegenGeldinstitutAuswahl> Geldinstitute { get; } = new();

        private VermoegenGeldinstitutAuswahl? _selectedGeldinstitut;
        public VermoegenGeldinstitutAuswahl? SelectedGeldinstitut
        {
            get => _selectedGeldinstitut;
            set
            {
                _selectedGeldinstitut = value;
                Model.GeldinstitutId = value?.Id;
                Model.GeldinstitutName = value?.Name ?? "";
            }
        }

        public VermoegenDepotDialog(VermoegenDepot? model = null)
        {
            InitializeComponent();

            Model = model == null
                ? new VermoegenDepot { Waehrung = "CHF", IstAktiv = true }
                : new VermoegenDepot
                {
                    Id = model.Id,
                    GeldinstitutId = model.GeldinstitutId,
                    GeldinstitutName = model.GeldinstitutName,
                    Name = model.Name,
                    Institut = model.Institut,
                    Waehrung = string.IsNullOrWhiteSpace(model.Waehrung) ? "CHF" : model.Waehrung,
                    IstAktiv = model.IstAktiv
                };

            LoadGeldinstitute();

            DataContext = this;

            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void LoadGeldinstitute()
        {
            Geldinstitute.Clear();

            foreach (var g in _db.VermoegenGeldinstituteGetForAuswahl())
                Geldinstitute.Add(g);

            if (Model.GeldinstitutId.HasValue)
                SelectedGeldinstitut = Geldinstitute.FirstOrDefault(g => g.Id == Model.GeldinstitutId.Value);
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show(
                    "Bitte einen Depotnamen erfassen.",
                    "Depot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                NameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(Model.Waehrung))
                Model.Waehrung = "CHF";

            Model.Name = Model.Name.Trim();
            Model.Institut = Model.Institut.Trim();
            Model.Waehrung = Model.Waehrung.Trim().ToUpperInvariant();

            DialogResult = true;
        }
    }
}