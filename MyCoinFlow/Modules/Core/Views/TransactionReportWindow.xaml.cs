using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;
using MyCoinFlow.ViewModels;
using System;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MyCoinFlow.Views
{
    public partial class TransactionReportWindow : BaseWindow
    {
        public TransactionReportWindow()
        {
            InitializeComponent();
            DataContext = new TransactionReportViewModel();
        }

        private TransactionReportViewModel ViewModel => (TransactionReportViewModel)DataContext;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void AdjustBudget_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var preview = ViewModel.ErstelleBudgetanpassungsVorschau();
                var dialog = new BudgetProjectionDialog(preview) { Owner = this };
                if (dialog.ShowDialog() != true)
                    return;

                var anzahl = ViewModel.BudgetanpassungenUebernehmen(preview.Zeilen);
                MessageBox.Show(
                    this,
                    $"{anzahl} Budgetwerte wurden erfolgreich übernommen.",
                    "Budget aktualisiert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Die Budgetwerte konnten nicht angepasst werden:\n" + ex.Message,
                    "Budget anpassen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentResult == null)
            {
                MessageBox.Show(
                    this,
                    "Bitte zuerst eine Auswertung erstellen.",
                    "Drucken",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true)
                    return;

                dialog.PrintTicket ??= new PrintTicket();
                dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
                dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

                var document = TransactionReportDocumentBuilder.Build(
                    ViewModel.CurrentResult,
                    dialog.PrintableAreaWidth,
                    dialog.PrintableAreaHeight);

                dialog.PrintDocument(
                    ((IDocumentPaginatorSource)document).DocumentPaginator,
                    ViewModel.CurrentResult.Optionen.Titel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Der Bericht konnte nicht gedruckt werden:\n" + ex.Message,
                    "Drucken",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
