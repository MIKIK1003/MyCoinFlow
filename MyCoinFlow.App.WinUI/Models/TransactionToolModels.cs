using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.WinUI.Models;

public sealed class AttachmentRecord
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string FolderRel { get; set; } = string.Empty;
    public string OcrStatus { get; set; } = "Ausstehend";
    public long? SizeBytes { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(OriginalName) ? FileName : OriginalName;
    public string SizeText => SizeBytes is null ? "" : SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / (1024d * 1024d):0.0} MB"
        : $"{SizeBytes / 1024d:0} KB";
}

public sealed class UnlinkedDocumentRecord
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime? DocumentDate { get; set; }
    public decimal? RecognizedAmount { get; set; }
    public long? SizeBytes { get; set; }
    public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title : !string.IsNullOrWhiteSpace(OriginalName) ? OriginalName : FileName;
    public string DateText => DocumentDate?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH")) ?? string.Empty;
    public string AmountText => RecognizedAmount?.ToString("C2", CultureInfo.GetCultureInfo("de-CH")) ?? string.Empty;
    public string SizeText => SizeBytes is null ? string.Empty : SizeBytes >= 1024 * 1024 ? $"{SizeBytes / (1024d * 1024d):0.0} MB" : $"{SizeBytes / 1024d:0} KB";
}

public sealed class DuplicateRecord : INotifyPropertyChanged
{
    private bool _delete;
    public bool Delete { get => _delete; set { if (_delete == value) return; _delete = value; PropertyChanged?.Invoke(this, new(nameof(Delete))); } }
    public int Group { get; set; }
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
    public string GroupText => $"Gruppe {Group}";
    public string DateText => Date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
    public string AmountText => Amount.ToString("C2", CultureInfo.GetCultureInfo("de-CH"));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ReportRow
{
    public int AccountId { get; set; }
    public int Number { get; set; }
    public string Account { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal BudgetYear { get; set; }
    public decimal Actual { get; set; }
    public decimal Projection { get; set; }
    public decimal DeltaYear => BudgetYear - Projection;
    public double Fulfillment => BudgetYear == 0 ? 0 : (double)(Actual / BudgetYear * 100m);
    public string NumberText => Number.ToString("D4", CultureInfo.InvariantCulture);
    public string BudgetText => BudgetYear.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    public string ActualText => Actual.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    public string ProjectionText => Projection.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    public string DeltaText => DeltaYear.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    public string FulfillmentText => $"{Fulfillment:N1} %";
}

public sealed class ImportPreviewRow : INotifyPropertyChanged
{
    private bool _selected = true;
    private bool _isIncome;
    private int? _accountId;
    private string _accountName = string.Empty;
    private int? _institutionId;
    private string _institutionName = string.Empty;
    private int? _addressId;
    private string _addressName = string.Empty;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public int? StagingId { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public bool IsIncome { get => _isIncome; set { if (_isIncome == value) return; _isIncome = value; Notify(nameof(IsIncome), nameof(DirectionText)); } }
    public string Description { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int? AccountId { get => _accountId; set { if (_accountId == value) return; _accountId = value; Notify(nameof(AccountId), nameof(IsComplete), nameof(StatusText)); } }
    public string AccountName { get => _accountName; set { if (_accountName == value) return; _accountName = value; Notify(nameof(AccountName)); } }
    public int? InstitutionId { get => _institutionId; set { if (_institutionId == value) return; _institutionId = value; Notify(nameof(InstitutionId), nameof(IsComplete), nameof(StatusText)); } }
    public string InstitutionName { get => _institutionName; set { if (_institutionName == value) return; _institutionName = value; Notify(nameof(InstitutionName)); } }
    public int? AddressId { get => _addressId; set { if (_addressId == value) return; _addressId = value; Notify(nameof(AddressId)); } }
    public string AddressName { get => _addressName; set { if (_addressName == value) return; _addressName = value; Notify(nameof(AddressName)); } }
    public bool IsComplete => AccountId.HasValue && InstitutionId.HasValue;
    public string StatusText => IsComplete ? "Bereit" : "Zuordnung fehlt";
    public string DateText => Date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
    public string AmountText => Amount.ToString("C2", CultureInfo.GetCultureInfo("de-CH"));
    public string DirectionText => IsIncome ? "Einnahme" : "Ausgabe";
    public void RefreshStatus() => Notify(nameof(IsComplete), nameof(StatusText), nameof(DirectionText));
    private void Notify(params string[] names) { foreach (var name in names) PropertyChanged?.Invoke(this, new(name)); }
    public event PropertyChangedEventHandler? PropertyChanged;
}
