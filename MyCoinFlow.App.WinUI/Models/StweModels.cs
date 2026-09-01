using MyCoinFlow.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.WinUI.Models;

public sealed class StweSetDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    public StweSetDisplayRow(StweSetRow value) => Value = value;
    public StweSetRow Value { get; }
    public int Id => Value.Id;
    public string DateText => Value.Datum.ToString("dd.MM.yyyy");
    public string Title => Value.Titel;
    public string TotalText => Value.Betrag.ToString("C", Swiss);
    public string DistributedText => Value.Verteilt.ToString("C", Swiss);
    public string RestText => Value.Rest.ToString("C", Swiss);
    public string TypeText => Value.IsCredit ? "Gutschrift" : "Belastung";
    public string StatusText => Value.IsClosed ? "Abgeschlossen" : "Offen";
}

public sealed class StweTransactionDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    public StweTransactionDisplayRow(Transaktion value) => Value = value;
    public Transaktion Value { get; }
    public string DateText => Value.Datum.ToString("dd.MM.yyyy");
    public string AmountText => Value.Betrag.ToString("C", Swiss);
    public string Address => Value.AdresseName ?? string.Empty;
    public string Bank => Value.BankName ?? string.Empty;
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class StweDistributionRow : INotifyPropertyChanged
{
    private int? _ownerId;
    private string _amountText = string.Empty;
    private string? _note;

    public StweDistributionRow(IReadOnlyList<StweEigentuemer> owners) => Owners = owners;
    public IReadOnlyList<StweEigentuemer> Owners { get; }
    public int? OwnerId { get => _ownerId; set => Set(ref _ownerId, value); }
    public string AmountText { get => _amountText; set { if (Set(ref _amountText, value ?? string.Empty)) OnPropertyChanged(nameof(Amount)); } }
    public decimal Amount => Parse(AmountText);
    public string? Note { get => _note; set => Set(ref _note, value); }
    public string Source { get; set; } = "MANUELL";

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public static decimal Parse(string? input)
    {
        var text = (input ?? string.Empty).Trim().Replace("’", "'").Replace(" ", string.Empty).Replace("'", string.Empty).Replace(",", ".");
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }
}

public sealed class StweMeterDataSetDisplayRow
{
    public StweMeterDataSetDisplayRow(StweZaehlerdatenSet value) => Value = value;
    public StweZaehlerdatenSet Value { get; }
    public string DateText => Value.ErfasstAm.ToString("dd.MM.yyyy");
    public string TypeText => Value.ErfassungsTyp == 1 ? "Monatswerte" : "Differenz";
    public string MonthsText => Value.ErfassungsTyp == 1 ? (Value.MonatsAnzahl?.ToString() ?? string.Empty) : "–";
    public string InvoiceKwhText => Value.RechnungKwhTotal?.ToString("0.###") ?? string.Empty;
    public string FeedInText => Value.RueckgespeistKwh?.ToString("0.###") ?? string.Empty;
    public string CreditText => Value.GutschriftChf?.ToString("0.00") ?? string.Empty;
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class StweMeterMonthEditorRow : INotifyPropertyChanged
{
    private string _text = string.Empty;
    public int MonthIndex { get; init; }
    public string Label => $"Monat {MonthIndex}";
    public string Text { get => _text; set { _text = value ?? string.Empty; PropertyChanged?.Invoke(this, new(nameof(Text))); PropertyChanged?.Invoke(this, new(nameof(Kwh))); } }
    public decimal Kwh => StweDistributionRow.Parse(Text);
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class StweMeterReadingEditorRow : INotifyPropertyChanged
{
    private string _newText = string.Empty;
    private bool _isMonthly;
    public int MeterId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? UnitId { get; init; }
    public string UnitText => UnitId?.ToString() ?? "–";
    public bool IsMonthly { get => _isMonthly; set { _isMonthly = value; PropertyChanged?.Invoke(this, new(nameof(IsMonthly))); PropertyChanged?.Invoke(this, new(nameof(IsDifference))); } }
    public bool IsDifference => !IsMonthly;
    public string NewText { get => _newText; set { _newText = value ?? string.Empty; Changed(); } }
    public decimal NewValue => StweDistributionRow.Parse(NewText);
    public ObservableCollection<StweMeterMonthEditorRow> Months { get; } = new();
    public decimal MonthSum => Months.Sum(value => value.Kwh);
    public string MonthsInfo => Months.Count == 0 ? "keine Monatswerte" : $"{Months.Count} Monat(e), Summe {MonthSum:0.###} kWh";
    public void EnsureMonthSlots(int count)
    {
        count = Math.Max(0, count);
        while (Months.Count < count)
        {
            var item = new StweMeterMonthEditorRow { MonthIndex = Months.Count + 1 };
            item.PropertyChanged += (_, _) => Changed();
            Months.Add(item);
        }
        while (Months.Count > count) Months.RemoveAt(Months.Count - 1);
        Changed();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() { PropertyChanged?.Invoke(this, new(nameof(NewText))); PropertyChanged?.Invoke(this, new(nameof(MonthSum))); PropertyChanged?.Invoke(this, new(nameof(MonthsInfo))); }
}

public sealed class StweEnergyDataSetOption
{
    public required StweZaehlerdatenSet Model { get; init; }
    public required string DisplayText { get; init; }
}

public sealed class StweEnergyDiffRow
{
    public int MeterId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? UnitId { get; init; }
    public int? AllocationKeyId { get; init; }
    public decimal OldValue { get; init; }
    public decimal NewValue { get; init; }
    public decimal DifferenceKwh => NewValue - OldValue;
    public string OldText => OldValue.ToString("0.###");
    public string NewText => NewValue.ToString("0.###");
    public string DifferenceText => DifferenceKwh.ToString("0.###");
}

public sealed class StweOwnerSummaryDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    public StweOwnerSummaryDisplayRow(StweOwnerSummaryRow value) => Value = value;
    public StweOwnerSummaryRow Value { get; }
    public string Name => Value.EigentuemerName;
    public string TotalText => Value.Summe.ToString("C", Swiss);
}

public sealed class StweOwnerDetailDisplayRow
{
    private static readonly CultureInfo Swiss = CultureInfo.GetCultureInfo("de-CH");
    public StweOwnerDetailDisplayRow(StweOwnerDetailRow value) => Value = value;
    public StweOwnerDetailRow Value { get; }
    public string DateText => Value.Datum.ToString("dd.MM.yyyy");
    public string Title => Value.Titel;
    public string Key => Value.Schluessel ?? string.Empty;
    public string AmountText => Value.Betrag.ToString("C", Swiss);
    public string Note => Value.Notiz ?? string.Empty;
}
