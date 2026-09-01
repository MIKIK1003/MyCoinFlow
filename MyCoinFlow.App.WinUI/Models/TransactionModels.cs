using System.Globalization;
using System.ComponentModel;

namespace MyCoinFlow.WinUI.Models;

public sealed class TransactionRecord
{
    public int Id { get; init; }
    public DateTime Datum { get; init; }
    public DateTime? BudgetDatum { get; init; }
    public int? VonKontoId { get; init; }
    public int? NachKontoId { get; init; }
    public int? VonKontoNummer { get; init; }
    public int? NachKontoNummer { get; init; }
    public decimal Betrag { get; init; }
    public string? Notiz { get; init; }
    public int? AdresseId { get; init; }
    public string? AdresseName { get; init; }
    public int? GeldinstitutId { get; init; }
    public string? BankName { get; init; }
    public string? ImportQuelle { get; init; }
    public string VonAnzeige { get; init; } = string.Empty;
    public string NachAnzeige { get; init; } = string.Empty;
    public int AttachmentCount { get; init; }

    public string DatumAnzeige => Datum.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
    public string BetragAnzeige => Betrag.ToString("C2", CultureInfo.GetCultureInfo("de-CH"));
    public string NummerAnzeige => $"#{Id}";
    public string KontextAnzeige
    {
        get
        {
            var values = new[] { AdresseName, BankName }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", values);
        }
    }
    public string AttachmentAnzeige => AttachmentCount == 0 ? string.Empty : AttachmentCount.ToString(CultureInfo.InvariantCulture);
    public double AttachmentOpacity => AttachmentCount == 0 ? 0d : 1d;
    public string BudgetDatumAnzeige => BudgetDatum.HasValue && BudgetDatum.Value.Date != Datum.Date
        ? $"Budget: {BudgetDatum.Value:dd.MM.yyyy}"
        : string.Empty;
    public bool HasAttachments => AttachmentCount > 0;
    public bool HasBudgetOverride => BudgetDatum.HasValue && BudgetDatum.Value.Date != Datum.Date;
}

public sealed class TransactionNumberRangeGroup : INotifyPropertyChanged
{
    private bool _isExpanded;

    public TransactionNumberRangeGroup(
        int? ruleId,
        string title,
        string direction,
        string summary,
        IReadOnlyList<TransactionRecord> entries,
        bool isExpanded = false)
    {
        RuleId = ruleId;
        Title = title;
        Direction = direction;
        Summary = summary;
        Entries = entries;
        _isExpanded = isExpanded;
    }

    public int? RuleId { get; }
    public string Title { get; }
    public string Direction { get; }
    public string Summary { get; }
    public IReadOnlyList<TransactionRecord> Entries { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LookupItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

public sealed record BudgetPeriod(DateTime Start, DateTime End, string Name);

public enum TransactionSummaryDirection
{
    Income,
    Expense
}

public sealed record TransactionSummaryAccount(
    int AccountId,
    int AccountNumber,
    TransactionSummaryDirection Direction);

public enum TransactionType
{
    BankToAccount,
    AccountToAccount,
    AccountToBank,
    AddressToAccount,
    AddressToBank
}

public sealed class TransactionDraft
{
    public int? Id { get; init; }
    public DateTime Datum { get; init; }
    public DateTime? BudgetDatum { get; init; }
    public int? VonKontoId { get; init; }
    public int? NachKontoId { get; init; }
    public decimal Betrag { get; init; }
    public string? Notiz { get; init; }
    public int? AdresseId { get; init; }
    public int? GeldinstitutId { get; init; }
}

public sealed record TransactionSearch(
    string? Term,
    DateTime? From,
    DateTime? To,
    string? Address);
