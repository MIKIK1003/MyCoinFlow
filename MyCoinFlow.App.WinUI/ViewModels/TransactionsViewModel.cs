using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.WinUI.ViewModels;

public sealed class TransactionsViewModel : INotifyPropertyChanged
{
    private readonly TransactionRepository _repository;
    private readonly TransactionReportCalculator _summaryCalculator = new();
    private string _searchText = string.Empty;
    private string _addressText = string.Empty;
    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    private TransactionRecord? _selectedTransaction;
    private bool _isBusy;
    private string? _statusMessage;
    private string? _errorMessage;
    private BudgetPeriod? _activeBudgetPeriod;
    private decimal _incomeAmount;
    private decimal _expenseAmount;
    private bool _isInitialized;

    public TransactionsViewModel(TransactionRepository repository)
    {
        _repository = repository;
    }

    public ObservableCollection<TransactionRecord> Transactions { get; } = new();
    public ObservableCollection<TransactionNumberRangeGroup> TransactionGroups { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public string AddressText
    {
        get => _addressText;
        set => Set(ref _addressText, value);
    }

    public DateTimeOffset? From
    {
        get => _from;
        set => Set(ref _from, value);
    }

    public DateTimeOffset? To
    {
        get => _to;
        set => Set(ref _to, value);
    }

    public TransactionRecord? SelectedTransaction
    {
        get => _selectedTransaction;
        set
        {
            if (Set(ref _selectedTransaction, value))
            {
                if (value is not null)
                {
                    var group = TransactionGroups.FirstOrDefault(candidate =>
                        candidate.Entries.Any(transaction => transaction.Id == value.Id));
                    if (group is not null)
                        group.IsExpanded = true;
                }
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanActOnSelection));
                OnPropertyChanged(nameof(SelectionContextText));
            }
        }
    }

    public bool HasSelection => SelectedTransaction is not null;
    public bool CanActOnSelection => HasSelection && IsNotBusy;
    public string SelectionContextText => SelectedTransaction is null
        ? "Keine Transaktion markiert"
        : $"Markiert: #{SelectedTransaction.Id} · {SelectedTransaction.DatumAnzeige} · {SelectedTransaction.BetragAnzeige}";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CanActOnSelection));
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasNoResults => _isInitialized && !IsBusy && !HasError && Transactions.Count == 0;
    public int ResultCount => Transactions.Count;
    public string ResultCountText => ResultCount == 1 ? "1 Transaktion" : $"{ResultCount:N0} Transaktionen";
    public decimal IncomeAmount => _incomeAmount;
    public string IncomeAmountText => IncomeAmount.ToString("C2", CultureInfo.GetCultureInfo("de-CH"));
    public decimal ExpenseAmount => _expenseAmount;
    public string ExpenseAmountText => ExpenseAmount.ToString("C2", CultureInfo.GetCultureInfo("de-CH"));
    public int AttachmentCount => Transactions.Count(transaction => transaction.HasAttachments);
    public string AttachmentCountText => AttachmentCount.ToString("N0");
    public string PeriodText => From.HasValue && To.HasValue
        ? $"{From.Value:dd.MM.yyyy} – {To.Value:dd.MM.yyyy}"
        : "Alle Zeiträume";

    public async Task InitializeAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _repository.VerifyDatabaseAsync();
            _activeBudgetPeriod = await _repository.GetActiveBudgetPeriodAsync();
            ApplyActivePeriod();
            await LoadCoreAsync();
        });
        _isInitialized = true;
        OnPropertyChanged(nameof(HasNoResults));
    }

    public Task RefreshAsync() => RunBusyAsync(LoadCoreAsync);

    /// <summary>Öffnet gezielt eine Buchung, auch wenn sie ausserhalb des aktiven Budgetzeitraums liegt.</summary>
    public async Task FocusTransactionAsync(int transactionId)
    {
        if (transactionId <= 0) return;
        SearchText = transactionId.ToString(CultureInfo.InvariantCulture);
        AddressText = string.Empty;
        From = null;
        To = null;
        SelectedTransaction = null;
        await RefreshAsync();
        SelectedTransaction = Transactions.FirstOrDefault(transaction => transaction.Id == transactionId);
        StatusMessage = SelectedTransaction is null
            ? $"Transaktion #{transactionId} wurde nicht gefunden."
            : $"Transaktion #{transactionId} wurde aus dem DMS geöffnet.";
    }

    public async Task ResetFiltersAsync()
    {
        SearchText = string.Empty;
        AddressText = string.Empty;
        ApplyActivePeriod();
        await RefreshAsync();
    }

    public void ClearError() => ErrorMessage = null;

    public void ReportError(Exception exception) => ErrorMessage = exception.Message;

    public void ReportStatus(string message) => StatusMessage = message;

    private void ApplyActivePeriod()
    {
        From = _activeBudgetPeriod is null
            ? null
            : new DateTimeOffset(_activeBudgetPeriod.Start.Date);
        To = _activeBudgetPeriod is null
            ? null
            : new DateTimeOffset(_activeBudgetPeriod.End.Date);
    }

    private async Task LoadCoreAsync()
    {
        ErrorMessage = null;
        var selectedId = SelectedTransaction?.Id;
        var search = new TransactionSearch(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            From?.Date,
            To?.Date,
            string.IsNullOrWhiteSpace(AddressText) ? null : AddressText.Trim());
        var recordsTask = _repository.SearchAsync(search);
        var summaryAccountsTask = _repository.GetSummaryAccountsAsync();
        var numberRangeRulesTask = _repository.GetNumberRangeRulesAsync();
        await Task.WhenAll(recordsTask, summaryAccountsTask, numberRangeRulesTask);
        var records = await recordsTask;
        var summaryAccounts = await summaryAccountsTask;
        var numberRangeRules = await numberRangeRulesTask;

        Transactions.Clear();
        foreach (var record in records)
            Transactions.Add(record);
        BuildTransactionGroups(records, numberRangeRules, selectedId);

        (_incomeAmount, _expenseAmount) = CalculateSummary(records, summaryAccounts);

        SelectedTransaction = selectedId.HasValue
            ? Transactions.FirstOrDefault(transaction => transaction.Id == selectedId.Value)
            : null;
        StatusMessage = $"Aktualisiert um {DateTime.Now:HH:mm}";
        NotifySummary();
    }

    private void BuildTransactionGroups(
        IReadOnlyList<TransactionRecord> records,
        IReadOnlyList<NumberRangeRule> rules,
        int? selectedId)
    {
        var expandedRuleIds = TransactionGroups
            .Where(group => group.IsExpanded && group.RuleId.HasValue)
            .Select(group => group.RuleId!.Value)
            .ToHashSet();
        var unassignedWasExpanded = TransactionGroups.Any(group =>
            group.IsExpanded && !group.RuleId.HasValue);
        TransactionGroups.Clear();

        var orderedRules = rules
            .OrderBy(rule => rule.RangeStart)
            .ThenBy(rule => rule.RangeEnd)
            .ThenBy(rule => rule.Id)
            .ToList();
        var assignments = orderedRules.ToDictionary(rule => rule, _ => new List<TransactionRecord>());
        var unassigned = new List<TransactionRecord>();

        foreach (var record in records)
        {
            var rule = FindMatchingRule(record.VonKontoNummer, orderedRules)
                       ?? FindMatchingRule(record.NachKontoNummer, orderedRules);
            if (rule is null)
                unassigned.Add(record);
            else
                assignments[rule].Add(record);
        }

        foreach (var rule in orderedRules)
        {
            var entries = assignments[rule];
            if (entries.Count == 0) continue;

            var title = string.IsNullOrWhiteSpace(rule.Bezeichnung)
                ? $"{rule.Richtung} {rule.RangeStart}-{rule.RangeEnd}"
                : rule.Bezeichnung!;
            TransactionGroups.Add(new TransactionNumberRangeGroup(
                rule.Id,
                title,
                rule.Richtung,
                $"{rule.RangeStart}-{rule.RangeEnd} · {FormatTransactionCount(entries.Count)} · {FormatAmount(entries)}",
                entries,
                expandedRuleIds.Contains(rule.Id)
                || (selectedId.HasValue && entries.Any(transaction => transaction.Id == selectedId.Value))));
        }

        if (unassigned.Count > 0)
        {
            TransactionGroups.Add(new TransactionNumberRangeGroup(
                null,
                "Keinem Nummernkreis zugeordnet",
                "Ohne Regel",
                $"{FormatTransactionCount(unassigned.Count)} · {FormatAmount(unassigned)}",
                unassigned,
                unassignedWasExpanded
                || (selectedId.HasValue && unassigned.Any(transaction => transaction.Id == selectedId.Value))));
        }
    }

    private static NumberRangeRule? FindMatchingRule(
        int? accountNumber,
        IReadOnlyCollection<NumberRangeRule> rules)
    {
        if (!accountNumber.HasValue) return null;
        return rules
            .Where(rule => accountNumber.Value >= rule.RangeStart && accountNumber.Value <= rule.RangeEnd)
            .OrderBy(rule => rule.RangeEnd - rule.RangeStart)
            .ThenBy(rule => rule.RangeStart)
            .FirstOrDefault();
    }

    private static string FormatTransactionCount(int count) =>
        count == 1 ? "1 Transaktion" : $"{count:N0} Transaktionen";

    private static string FormatAmount(IEnumerable<TransactionRecord> entries) =>
        entries.Sum(transaction => transaction.Betrag)
            .ToString("C2", CultureInfo.GetCultureInfo("de-CH"));

    private (decimal Income, decimal Expense) CalculateSummary(
        IReadOnlyCollection<TransactionRecord> records,
        IReadOnlyCollection<TransactionSummaryAccount> summaryAccounts)
    {
        if (records.Count == 0 || summaryAccounts.Count == 0)
            return (0m, 0m);

        var usedAccountIds = records
            .SelectMany(record => new[] { record.VonKontoId, record.NachKontoId })
            .Where(accountId => accountId.HasValue)
            .Select(accountId => accountId!.Value)
            .ToHashSet();
        var accounts = summaryAccounts
            .Where(account => usedAccountIds.Contains(account.AccountId))
            .Select(account => new TransactionReportAccount
            {
                KontoId = account.AccountId,
                Kontonummer = account.AccountNumber,
                Richtung = account.Direction == TransactionSummaryDirection.Income
                    ? TransactionReportDirection.Einnahme
                    : TransactionReportDirection.Ausgabe
            })
            .ToList();
        if (accounts.Count == 0)
            return (0m, 0m);

        var reportTransactions = records.Select(record => new Transaktion
        {
            Id = record.Id,
            Datum = record.Datum,
            BudgetDatum = record.BudgetDatum,
            VonKontoId = record.VonKontoId,
            NachKontoId = record.NachKontoId,
            Betrag = record.Betrag,
            Notiz = record.Notiz,
            AdresseId = record.AdresseId,
            AdresseName = record.AdresseName,
            GeldinstitutId = record.GeldinstitutId,
            BankName = record.BankName,
            ImportQuelle = record.ImportQuelle
        }).ToList();
        var effectiveDates = records
            .Select(record => (record.BudgetDatum ?? record.Datum).Date)
            .ToList();
        var from = effectiveDates.Min();
        var to = effectiveDates.Max();
        var options = new TransactionReportOptions
        {
            BudgetVon = from,
            BudgetBis = to,
            AuswertungVon = from,
            AuswertungBis = to,
            Modus = TransactionReportMode.IstMitHochrechnung,
            Gruppierung = TransactionReportGrouping.Einzelkonto
        };
        var result = _summaryCalculator.Berechnen(options, accounts, reportTransactions);
        return (result.Einnahmen.IstZeitraum ?? 0m, result.Ausgaben.IstZeitraum ?? 0m);
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(IncomeAmount));
        OnPropertyChanged(nameof(IncomeAmountText));
        OnPropertyChanged(nameof(ExpenseAmount));
        OnPropertyChanged(nameof(ExpenseAmountText));
        OnPropertyChanged(nameof(AttachmentCount));
        OnPropertyChanged(nameof(AttachmentCountText));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
