using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BudgetPeriodEditorDialog : ContentDialog
{
    public BudgetPeriodEditorDialog(Budgetzeitraum? period = null)
    {
        InitializeComponent();
        HeadingText.Text = period is null ? "Neuen Budgetzeitraum erfassen" : $"{period.Bezeichnung} bearbeiten";
        if (period is null) return;

        NameBox.Text = period.Bezeichnung;
        StartPicker.SelectedDate = new DateTimeOffset(period.Startdatum);
        EndPicker.SelectedDate = new DateTimeOffset(period.Enddatum);
        ActiveCheckBox.IsChecked = period.IstAktiv;
    }

    public string PeriodName { get; private set; } = string.Empty;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public bool IsActive { get; private set; }
    public bool Accepted { get; private set; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        EditorError.IsOpen = false;
        if (string.IsNullOrWhiteSpace(NameBox.Text) ||
            StartPicker.SelectedDate is null ||
            EndPicker.SelectedDate is null)
        {
            args.Cancel = true;
            EditorError.Message = "Bitte alle Pflichtfelder korrekt ausfüllen.";
            EditorError.IsOpen = true;
            return;
        }

        PeriodName = NameBox.Text.Trim();
        StartDate = StartPicker.SelectedDate.Value.Date;
        EndDate = EndPicker.SelectedDate.Value.Date;
        IsActive = ActiveCheckBox.IsChecked == true;
        Accepted = true;
    }
}
