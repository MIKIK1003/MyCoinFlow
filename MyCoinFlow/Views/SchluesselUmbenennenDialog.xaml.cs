using MyCoinFlow.UI.Base; // NEU
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    public partial class SchluesselUmbenennenDialog : BaseWindow // NEU
    {
        public string AlteBezeichnung { get; }
        public string NeueBezeichnung { get; set; }

        public SchluesselUmbenennenDialog(string currentName)
        {
            InitializeComponent();
            AlteBezeichnung = $"Aktuell: {currentName}";
            NeueBezeichnung = currentName;
            DataContext = this;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}