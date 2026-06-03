using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class VermoegenSymbolSucheWindow
    {
        private readonly VermoegenSymbolSucheViewModel _vm;

        public SymbolSucheResult? Ergebnis => _vm.Ergebnis;

        public VermoegenSymbolSucheWindow(string suchtext, string apiKey)
        {
            InitializeComponent();

            _vm = new VermoegenSymbolSucheViewModel(suchtext, apiKey, this);
            DataContext = _vm;

            Loaded += async (_, _) =>
            {
                SuchBox.Focus();
                SuchBox.SelectAll();
                await _vm.AutoSucheAsync();
            };
        }
    }
}