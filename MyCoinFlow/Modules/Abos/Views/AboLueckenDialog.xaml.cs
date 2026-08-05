using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class AboLueckenDialog
    {
        public AboLueckenDialog(List<AboLueckeKandidat> kandidaten, List<System.DateTime> lueckenOhneKandidat)
        {
            InitializeComponent();

            LueckenGrid.ItemsSource = kandidaten;

            if (lueckenOhneKandidat.Count > 0)
            {
                var daten = string.Join(", ", lueckenOhneKandidat
                    .OrderBy(d => d)
                    .Select(d => d.ToString("dd.MM.yyyy")));

                InfoText.Text += $"\n\nOhne passenden Vorschlag: {daten} – " +
                                 "diese Zahlungen fehlen ggf. wirklich (z.B. anderes Konto/Karte noch nicht importiert) " +
                                 "oder weichen zu stark ab. Sie können über «Zahlung zuordnen» manuell gesucht werden.";
            }
        }

        private void Zuordnen_Click(object sender, RoutedEventArgs e)
        {
            // Offene Zellen-Edits (Checkboxen) übernehmen
            LueckenGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            DialogResult = true;
        }
    }
}
