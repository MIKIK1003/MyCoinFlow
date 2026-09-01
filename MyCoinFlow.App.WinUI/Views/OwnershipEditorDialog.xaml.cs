using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class OwnershipEditorDialog : ContentDialog
{
    public OwnershipEditorDialog(IReadOnlyList<StweEigentuemer> owners, StweEinheitEigentumRow? source = null)
    {
        InitializeComponent();
        OwnerBox.ItemsSource = owners;
        FromPicker.SelectedDate = source is null ? new DateTimeOffset(DateTime.Today) : new DateTimeOffset(source.GueltigVon);
        ToPicker.SelectedDate = source?.GueltigBis is DateTime to ? new DateTimeOffset(to) : null;
        if (source is not null) OwnerBox.SelectedValue = source.EigentuemerId;
    }
    public int OwnerId { get; private set; }
    public DateTime From { get; private set; }
    public DateTime? To { get; private set; }
    public bool Accepted { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (OwnerBox.SelectedValue is not int ownerId)
        {
            args.Cancel = true; ShowError("Bitte Eigentümer auswählen."); return;
        }
        if (FromPicker.SelectedDate is null)
        {
            args.Cancel = true; ShowError("Bitte 'Von' setzen."); return;
        }
        var from = FromPicker.SelectedDate.Value.Date;
        var to = ToPicker.SelectedDate?.Date;
        if (to.HasValue && to.Value < from)
        {
            args.Cancel = true; ShowError("'Bis' darf nicht vor 'Von' liegen."); return;
        }
        OwnerId = ownerId; From = from; To = to; Accepted = true;
    }
    private void ShowError(string message) { EditorError.Message = message; EditorError.IsOpen = true; }
}
