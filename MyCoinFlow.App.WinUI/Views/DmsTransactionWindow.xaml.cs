using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;
using Windows.Graphics;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

/// <summary>
/// Gemeinsames Auswahlfenster für manuelle DMS-Zuweisungen und mehrdeutige Treffer des
/// Hintergrund-Watchers. Entspricht funktional dem WPF-Dialog, bleibt als echtes Window aber
/// auch dann verfügbar, wenn in der Hauptoberfläche bereits ein ContentDialog geöffnet ist.
/// </summary>
public sealed partial class DmsTransactionWindow : PersistentWindow
{
    private readonly DatabaseService _database = new();
    private readonly AttachmentService _attachments = new();
    private readonly DmsTransactionSelectionRequest _request;
    private readonly TaskCompletionSource<int?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _ready;

    public DmsTransactionWindow(DmsTransactionSelectionRequest request)
    {
        InitializeComponent();
        _request = request;
        AppWindow.Resize(new SizeInt32(1040, 720));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 780;
            presenter.PreferredMinimumHeight = 520;
        }

        Closed += (_, _) => _completion.TrySetResult(null);

        DocumentTitleText.Text = request.DocumentTitle;
        DocumentFileText.Text = $"Datei: {request.FileName}";
        DocumentFactsText.Text =
            $"Dokumentdatum: {(request.DocumentDate.HasValue ? request.DocumentDate.Value.ToString("dd.MM.yyyy") : "–")}" +
            $"  ·  Erkannter Betrag: {(request.RecognizedAmount.HasValue ? request.RecognizedAmount.Value.ToString("N2", CultureInfo.CurrentCulture) : "–")}" +
            $"  ·  Adresse: {(!string.IsNullOrWhiteSpace(request.Address) ? request.Address : "–")}";
        OpenDocumentButton.IsEnabled = request.AttachmentId > 0;

        if (request.Candidates.Count > 0)
        {
            HintText.Text = $"Wähle die Transaktion, mit der dieses Dokument verknüpft werden soll. {request.Candidates.Count} möglicher Treffer.";
            Fill(request.Candidates);
        }
        else
        {
            HintText.Text = "Suche die Transaktion, mit der dieses Dokument verknüpft werden soll.";
        }

        _ready = true;
    }

    public Task<int?> ShowAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        Search();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => Search();

    private void OnOpenDocumentClick(object sender, RoutedEventArgs e)
    {
        try { _attachments.OpenAttachment(_request.AttachmentId); }
        catch (Exception exception)
        {
            MessageBar.Message = "Dokument konnte nicht geöffnet werden: " + exception.Message;
            MessageBar.Severity = InfoBarSeverity.Error;
            MessageBar.IsOpen = true;
        }
    }

    private void OnIncludeLinkedChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) Search();
    }

    private void Search()
    {
        try
        {
            MessageBar.IsOpen = false;
            decimal? amount = null;
            var amountText = (AmountBox.Text ?? string.Empty).Trim().Replace("'", string.Empty);
            if (!string.IsNullOrWhiteSpace(amountText) &&
                (decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ||
                 decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)))
            {
                amount = parsed;
            }

            var includeLinked = IncludeLinkedBox.IsChecked == true;
            var result = _database.SearchTransaktionenForZuordnung(
                string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                amount,
                FromPicker.Date?.Date,
                ToPicker.Date?.Date,
                nurOhneDokument: !includeLinked);
            Fill(result);

            HintText.Text = result.Count == 0
                ? includeLinked
                    ? "Keine Treffer."
                    : "Keine Treffer. Hinweis: Transaktionen mit bereits verknüpftem Dokument werden ausgeblendet (Checkbox einschalten, um sie zu sehen)."
                : result.Count == 1
                    ? "1 passende Transaktion gefunden."
                    : $"{result.Count} passende Transaktionen gefunden.";
        }
        catch (Exception exception)
        {
            MessageBar.Message = "Suche fehlgeschlagen: " + exception.Message;
            MessageBar.Severity = InfoBarSeverity.Error;
            MessageBar.IsOpen = true;
        }
    }

    private void Fill(IEnumerable<Transaktion> transactions)
    {
        CandidatesList.ItemsSource = transactions.Select(transaction => new DmsTransactionRow(
            transaction.Id,
            transaction.Datum.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
            transaction.Betrag.ToString("N2", CultureInfo.CurrentCulture),
            transaction.AdresseName ?? transaction.BankName ?? "–",
            transaction.Notiz)).ToList();
    }

    private void OnCandidateDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => CompleteSelection();

    private void OnAssignClick(object sender, RoutedEventArgs e) => CompleteSelection();

    private void CompleteSelection()
    {
        if (CandidatesList.SelectedItem is not DmsTransactionRow row)
        {
            MessageBar.Message = "Bitte zuerst eine Transaktion auswählen.";
            MessageBar.Severity = InfoBarSeverity.Informational;
            MessageBar.IsOpen = true;
            return;
        }

        _completion.TrySetResult(row.Id);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(null);
        Close();
    }
}

public sealed record DmsTransactionRow(int Id, string Date, string Amount, string Who, string? Note)
{
    public string Number => $"#{Id}";
}
