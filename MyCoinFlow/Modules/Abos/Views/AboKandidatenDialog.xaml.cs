using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class AboKandidatenDialog
    {
        public AboKandidatenDialog(List<AboKandidat> kandidaten)
        {
            InitializeComponent();

            KandidatenGrid.ItemsSource = kandidaten;
        }

        private void Uebernehmen_Click(object sender, RoutedEventArgs e)
        {
            // Offene Zellen-Edits (Checkboxen) übernehmen
            KandidatenGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            DialogResult = true;
        }
    }
}
