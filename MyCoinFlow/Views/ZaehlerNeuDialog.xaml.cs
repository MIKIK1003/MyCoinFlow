using MyCoinFlow.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class ZaehlerNeuDialog : Window, INotifyPropertyChanged
    {
        public StweZaehler Model { get; } = new();

        public string HeaderText => Model.Id > 0 ? "Zähler bearbeiten" : "Zähler neu";

        public ObservableCollection<string> TypOptions { get; } = new()
        {
            "DIREKT",
            "ALLG",
            "HEIZ",
            "EVU"
        };

        public ObservableCollection<StweEinheit> Einheiten { get; } = new();

        private string _selectedTyp = "DIREKT";
        public string SelectedTyp
        {
            get => _selectedTyp;
            set
            {
                _selectedTyp = (value ?? "DIREKT").Trim().ToUpperInvariant();
                Model.Typ = _selectedTyp;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEinheitEnabled));
                OnPropertyChanged(nameof(EinheitOpacity));

                // Wenn nicht DIREKT -> Einheit leeren
                if (!IsEinheitEnabled)
                {
                    SelectedEinheit = null;
                    Model.EinheitId = null;
                }
            }
        }

        private StweEinheit? _selectedEinheit;
        public StweEinheit? SelectedEinheit
        {
            get => _selectedEinheit;
            set
            {
                _selectedEinheit = value;
                OnPropertyChanged();
                Model.EinheitId = _selectedEinheit?.Id;
            }
        }

        public bool IsEinheitEnabled => string.Equals(SelectedTyp, "DIREKT", StringComparison.OrdinalIgnoreCase);
        public double EinheitOpacity => IsEinheitEnabled ? 1.0 : 0.5;

        public ZaehlerNeuDialog(int liegenschaftId, ObservableCollection<StweEinheit> einheiten)
        {
            InitializeComponent();

            // Einheiten kopieren (defensiv, damit Dialog unabhängig bleibt)
            if (einheiten != null)
            {
                foreach (var e in einheiten)
                    Einheiten.Add(e);
            }

            Model.LiegenschaftId = liegenschaftId;
            Model.Typ = "DIREKT";
            _selectedTyp = "DIREKT";

            DataContext = this;
        }

        // Für Bearbeiten: Model-Werte übernehmen
        public void SetModel(StweZaehler existing)
        {
            if (existing == null) return;

            Model.Id = existing.Id;
            Model.LiegenschaftId = existing.LiegenschaftId;
            Model.Name = existing.Name ?? "";
            Model.Typ = (existing.Typ ?? "").Trim().ToUpperInvariant();
            Model.EinheitId = existing.EinheitId;
            Model.Notiz = existing.Notiz;

            SelectedTyp = string.IsNullOrWhiteSpace(Model.Typ) ? "DIREKT" : Model.Typ;

            if (Model.EinheitId.HasValue)
                SelectedEinheit = Einheiten.FirstOrDefault(x => x.Id == Model.EinheitId.Value);
            else
                SelectedEinheit = null;

            OnPropertyChanged(nameof(HeaderText));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
                return;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private bool Validate()
        {
            Model.Name = (Model.Name ?? "").Trim();
            Model.Typ = (Model.Typ ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte einen Namen erfassen.", "Zähler", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (!TypOptions.Contains(Model.Typ))
            {
                MessageBox.Show("Bitte einen gültigen Typ wählen (DIREKT/ALLG/HEIZ/EVU).", "Zähler",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.Equals(Model.Typ, "DIREKT", StringComparison.OrdinalIgnoreCase))
            {
                if (!Model.EinheitId.HasValue || Model.EinheitId.Value <= 0)
                {
                    MessageBox.Show("Bei Typ DIREKT muss eine Einheit gewählt werden.", "Zähler",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                // ALLG/HEIZ/EVU -> EinheitId muss leer sein
                Model.EinheitId = null;
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
