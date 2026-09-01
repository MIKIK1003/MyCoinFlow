using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Models;

public sealed class QuickAccountSettingRow
{
    public int Id { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}

public sealed class CategoryAccountSettingRow
{
    public int Id { get; init; }
    public string Category { get; init; } = string.Empty;
    public int? AccountId { get; set; }
    public IReadOnlyList<KontoLookup> Accounts { get; init; } = Array.Empty<KontoLookup>();
}
