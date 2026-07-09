using System;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;

namespace MyCoinFlow.Views
{
    public partial class BudgetDetailView : UserControl
    {
        public BudgetDetailView()
        {
            InitializeComponent();
        }

        // Speichert sofort, wenn eine Zelle (Budget) bestätigt wird (Tab/Enter/Fokus weg)
        private void GridBudget_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (e.Row?.Item is not BudgetKontoRow rowItem)
                return;

            // Quelle aktualisieren (Converter entscheidet, ob der Wert übernommen wird)
            if (e.Column is DataGridTextColumn && e.EditingElement is TextBox tb)
            {
                BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
            }

            // Asynchron nach dem Commit speichern (keine Reentrancy)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is BudgetDetailViewModel vm)
                    vm.SaveOne(rowItem);
            }), DispatcherPriority.Background);
        }

        // Extra-Sicherheit: Wenn die ganze Zeile per Enter bestätigt wird, speichern wir ebenfalls.
        private void GridBudget_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (e.Row?.Item is not BudgetKontoRow rowItem)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is BudgetDetailViewModel vm)
                    vm.SaveOne(rowItem);
            }), DispatcherPriority.Background);
        }
    }
}
