using MyCoinFlow.Helpers;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class VermoegenSymbolSucheViewModel : BaseViewModel
    {
        private readonly KursService _kursService = new();
        private readonly string _apiKey;
        private readonly Window _ownerWindow;

        public ObservableCollection<SymbolSucheResult> Treffer { get; } = new();

        private SymbolSucheResult? _selectedTreffer;
        public SymbolSucheResult? SelectedTreffer
        {
            get => _selectedTreffer;
            set
            {
                _selectedTreffer = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _suchtext = "";
        public string Suchtext
        {
            get => _suchtext;
            set
            {
                _suchtext = value ?? "";
                OnPropertyChanged();
            }
        }

        private string _statusText = "Bereit.";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public SymbolSucheResult? Ergebnis { get; private set; }

        public ICommand SucheCommand { get; }
        public ICommand UebernehmenCommand { get; }

        public VermoegenSymbolSucheViewModel(string suchtext, string apiKey, Window ownerWindow)
        {
            Suchtext = suchtext ?? "";
            _apiKey = apiKey ?? "";
            _ownerWindow = ownerWindow;

            SucheCommand = new RelayCommand(async _ => await SucheAsync());
            UebernehmenCommand = new RelayCommand(_ => Uebernehmen(), _ => SelectedTreffer != null);
        }

        public async Task AutoSucheAsync()
        {
            if (!string.IsNullOrWhiteSpace(Suchtext))
                await SucheAsync();
        }

        private async Task SucheAsync()
        {
            Treffer.Clear();
            SelectedTreffer = null;

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                StatusText = "Kein API-Key vorhanden.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Suchtext))
            {
                StatusText = "Bitte Suchtext erfassen.";
                return;
            }

            StatusText = "Suche läuft...";

            var result = await _kursService.SucheInstrumenteAsync(Suchtext, _apiKey);

            foreach (var r in result
                         .OrderBy(r => r.Boerse)
                         .ThenBy(r => r.Symbol)
                         .Take(100))
            {
                Treffer.Add(r);
            }

            SelectedTreffer = Treffer.FirstOrDefault();

            StatusText = Treffer.Count == 0
                ? "Keine Treffer gefunden."
                : $"{Treffer.Count} Treffer gefunden.";
        }

        private void Uebernehmen()
        {
            if (SelectedTreffer == null)
                return;

            Ergebnis = SelectedTreffer;
            _ownerWindow.DialogResult = true;
        }
    }
}