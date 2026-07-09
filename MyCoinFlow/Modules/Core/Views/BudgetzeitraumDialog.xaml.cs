using System.Windows;
using MyCoinFlow.UI.Base; // NEU

namespace MyCoinFlow.Views
{
    public partial class BudgetzeitraumDialog : BaseWindow // NEU
    {
        public BudgetzeitraumDialog()
        {
            InitializeComponent();
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BezeichnungBox.Text) ||
                StartdatumPicker.SelectedDate == null ||
                EnddatumPicker.SelectedDate == null)
            {
                MessageBox.Show("Bitte alle Pflichtfelder korrekt ausfüllen.");
                return;
            }

            this.DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}