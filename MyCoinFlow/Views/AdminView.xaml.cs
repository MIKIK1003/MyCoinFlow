using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AdminView : UserControl
    {
        private async void Copy_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Copy_StatusText.Text = "";

                // Quelle/Ziel robust auslesen
                var sourceDb = (Copy_SourceDbCombo.Text ?? "").Trim();
                var targetDb = (Copy_TargetDbCombo.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(sourceDb))
                {
                    Copy_StatusText.Text = "Bitte eine QUELLE wählen.";
                    return;
                }
                if (string.IsNullOrWhiteSpace(targetDb))
                {
                    Copy_StatusText.Text = "Bitte ein ZIEL wählen.";
                    return;
                }
                if (string.Equals(sourceDb, targetDb, StringComparison.OrdinalIgnoreCase))
                {
                    Copy_StatusText.Text = "Quelle und Ziel dürfen nicht identisch sein.";
                    return;
                }

                // Optionen zusammenstellen
                var opt = new DbCopyOptions
                {
                    CopyNumberRanges = Copy_CbNummernkreise.IsChecked == true,
                    CopyKontenstruktur = Copy_CbKonten.IsChecked == true,
                    CopyAdressen = Copy_CbAdressen.IsChecked == true,
                    CopyAliase = Copy_CbAliase.IsChecked == true,
                    CopyGeldinstitute = Copy_CbGeldinst.IsChecked == true,
                    CopyImportSchemas = Copy_CbImport.IsChecked == true,
                    CopyKategorieKonto = Copy_CbKatMap.IsChecked == true,
                    CreateBudgetzeitraum = Copy_CbBudget.IsChecked == true,
                    BudgetYear = DateTime.Today.Year
                };

                // UI sperren
                ((Button)sender).IsEnabled = false;
                Copy_StatusText.Text = $"Kopiere von '{sourceDb}' → '{targetDb}' …";

                var svc = new DbCopyService();
                // Ziel sollte in der UI bereits existieren → createTargetIfMissing:false
                await svc.CopyAsync(sourceDb, targetDb, opt, createTargetIfMissing: false);

                Copy_StatusText.Text = "Kopieren erfolgreich abgeschlossen.";
            }
            catch (Exception ex)
            {
                Copy_StatusText.Text = "Fehler beim Kopieren: " + ex.Message;
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
            }
        }
    }
}
