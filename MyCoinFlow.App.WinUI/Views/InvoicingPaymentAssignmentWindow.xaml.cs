using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Import;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingPaymentAssignmentWindow : PersistentWindow
{
    private readonly BankImportItem _item;
    private readonly InvoicingPaymentRepository _repository = new();
    private bool _busy;
    private bool _loaded;

    public InvoicingPaymentAssignmentWindow(BankImportItem item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        InitializeComponent();
        ConfigureDpiAwareSizing(RootGrid, 1160, 800, 760, 600);
        Activated += OnActivated;
        AmountText.Text = $"{item.Amount:N2} {item.Currency}";
        DateText.Text = item.BookingDate.ToString("dd.MM.yyyy");
        ReferenceText.Text = string.IsNullOrWhiteSpace(item.StructuredReference)
            ? string.IsNullOrWhiteSpace(item.ServiceRef) ? "—" : item.ServiceRef
            : item.StructuredReference;
        AccountText.Text = string.IsNullOrWhiteSpace(item.AccountIban) ? "—" : item.AccountIban;
        CounterpartyText.Text = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "—" : item.CounterpartyName;
        NarrativeText.Text = string.IsNullOrWhiteSpace(item.Text) ? "—" : item.Text;
    }

    public bool Changed { get; private set; }
    public event EventHandler<InvoicingPaymentBookingResult>? PaymentCompleted;

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadAsync();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var workspace = await _repository.LoadWorkspaceAsync(_item);
            CandidatesList.ItemsSource = workspace.Candidates;
            var proposal = workspace.Candidates.FirstOrDefault(candidate => candidate.IsSuggested)
                           ?? workspace.Candidates.FirstOrDefault();
            CandidatesList.SelectedItem = proposal;
            StatusBar.Severity = workspace.Candidates.Count == 0
                ? InfoBarSeverity.Warning
                : proposal?.IsSuggested == true
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Informational;
            StatusBar.Message = workspace.Candidates.Count == 0
                ? "Es gibt keinen offenen Rechnungskandidaten. Die Zeile kann in den Klärbestand übernommen werden."
                : proposal?.IsSuggested == true
                    ? $"Eindeutiger Vorschlag: {proposal.DocumentNumber}. Vor dem Buchen bitte kontrollieren."
                    : $"{workspace.Candidates.Count} offene Rechnung(en) gefunden; bitte bewusst auswählen.";
            if (workspace.OpenClarificationCount > 0)
                StatusBar.Message += $" Für diese CAMT-Zeile bestehen {workspace.OpenClarificationCount} offene Klärfall-Einträge.";
        }
        catch (Exception exception)
        {
            CandidatesList.ItemsSource = null;
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var candidate = CandidatesList.SelectedItem as InvoicingPaymentCandidate;
        BookButton.IsEnabled = !_busy && candidate?.CanBook == true;
        if (candidate is { CanBook: false })
            ShowStatus(candidate.BlockingReason, InfoBarSeverity.Warning);
    }

    private async void OnBookClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CandidatesList.SelectedItem is not InvoicingPaymentCandidate candidate ||
            !candidate.CanBook || _item.StagingId is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _repository.BookAsync(
                _item.StagingId.Value, candidate.DocumentId, candidate.MatchKind);
            Changed = true;
            PaymentCompleted?.Invoke(this, result);
            ShowStatus(result.Summary, result.SurplusAmount > 0m
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success);
            BookButton.IsEnabled = false;
            ClarificationButton.IsEnabled = false;
            CandidatesList.IsEnabled = false;
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnClarificationClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _item.StagingId is null) return;
        var candidate = CandidatesList.SelectedItem as InvoicingPaymentCandidate;
        var reason = _item.Direction != KreditDebit.Credit
            ? InvoicingClarificationReasons.WrongDirection
            : candidate is { CanBook: false } &&
              !string.Equals(_item.Currency, candidate.CurrencyCode, StringComparison.OrdinalIgnoreCase)
                ? InvoicingClarificationReasons.CurrencyMismatch
                : candidate is { CanBook: false } &&
                  (candidate.PaymentAccountId <= 0 || candidate.GeldinstitutId <= 0)
                    ? InvoicingClarificationReasons.Configuration
                : CandidatesList.Items.Count == 0
                    ? InvoicingClarificationReasons.NoMatch
                    : InvoicingClarificationReasons.Ambiguous;
        var narrative = string.IsNullOrWhiteSpace(ClarificationTextBox.Text)
            ? candidate is null
                ? "Keine eindeutige offene Rechnung gefunden."
                : $"Zuordnung zu {candidate.DocumentNumber} ist noch zu klären: {candidate.BlockingReason}"
            : ClarificationTextBox.Text.Trim();

        SetBusy(true);
        try
        {
            await _repository.AddToClarificationAsync(
                _item.StagingId.Value, candidate?.DocumentId, reason, narrative);
            Changed = true;
            ShowStatus("Die CAMT-Zeile bleibt offen und ist im Klärbestand dokumentiert.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CandidatesList.IsEnabled = !busy;
        ClarificationButton.IsEnabled = !busy && _item.StagingId.HasValue;
        BookButton.IsEnabled = !busy &&
                               CandidatesList.SelectedItem is InvoicingPaymentCandidate { CanBook: true };
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
