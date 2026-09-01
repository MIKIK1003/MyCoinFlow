using MyCoinFlow.Models;
using System.Globalization;

namespace MyCoinFlow.WinUI.Models;

public sealed class PropertyDisplayRow
{
    public PropertyDisplayRow(StweLiegenschaft value) => Value = value;
    public StweLiegenschaft Value { get; }
    public int Id => Value.Id;
    public string Name => Value.Name;
    public string Street => Value.Strasse ?? string.Empty;
    public string Location => string.Join(" ", new[] { Value.PLZ, Value.Ort }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class PropertyUnitDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public PropertyUnitDisplayRow(StweEinheit value) => Value = value;
    public StweEinheit Value { get; }
    public int Id => Value.Id;
    public string Name => Value.Bezeichnung;
    public string Type => Value.Typ ?? string.Empty;
    public string Mea => Value.MeaPromille?.ToString("N2", SwissCulture) ?? string.Empty;
    public string Area => Value.FlaecheM2?.ToString("N2", SwissCulture) ?? string.Empty;
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class PropertyOwnerDisplayRow
{
    public PropertyOwnerDisplayRow(StweEigentuemer value) => Value = value;
    public StweEigentuemer Value { get; }
    public int Id => Value.Id;
    public string Name => Value.Name;
    public string Email => Value.Email ?? string.Empty;
    public string Phone => Value.Telefon ?? string.Empty;
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class OwnershipDisplayRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public OwnershipDisplayRow(StweEinheitEigentumRow value) => Value = value;
    public StweEinheitEigentumRow Value { get; }
    public int Id => Value.Id;
    public string Owner => Value.EigentuemerName;
    public string From => Value.GueltigVon.ToString("dd.MM.yyyy", SwissCulture);
    public string To => Value.GueltigBis?.ToString("dd.MM.yyyy", SwissCulture) ?? "–";
}

public sealed class AllocationKeyDisplayRow
{
    public AllocationKeyDisplayRow(StweSchluessel value) => Value = value;
    public StweSchluessel Value { get; }
    public int Id => Value.Id;
    public string Name => Value.Name;
    public string Mode => Value.Modus;
}

public sealed class MeterDisplayRow
{
    public MeterDisplayRow(
        StweZaehler value,
        IReadOnlyDictionary<int, string>? unitNames = null,
        IReadOnlyDictionary<int, string>? allocationKeyNames = null)
    {
        Value = value;
        Unit = value.EinheitId.HasValue && unitNames?.TryGetValue(value.EinheitId.Value, out var unitName) == true
            ? unitName
            : value.EinheitId.HasValue
                ? $"Einheit #{value.EinheitId.Value}"
                : "–";
        AllocationKey = value.SchluesselId.HasValue && allocationKeyNames?.TryGetValue(value.SchluesselId.Value, out var keyName) == true
            ? keyName
            : value.SchluesselId.HasValue
                ? $"Schlüssel #{value.SchluesselId.Value}"
                : "–";
    }
    public StweZaehler Value { get; }
    public int Id => Value.Id;
    public string Name => Value.Name;
    public string Type => Value.Typ;
    public string Unit { get; }
    public string AllocationKey { get; }
    public string Note => Value.Notiz ?? string.Empty;
}

public sealed class AllocationLineEditorRow
{
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    public int? UnitId { get; set; }
    public int OwnerId { get; set; }
    public string PercentageText { get; set; } = "0.0000";

    public static AllocationLineEditorRow From(StweSchluesselLine value) => new()
    {
        UnitId = value.EinheitId,
        OwnerId = value.EigentuemerId,
        PercentageText = value.AnteilProzent.ToString("N4", SwissCulture)
    };

    public static AllocationLineEditorRow From(StweZaehlerLine value) => new()
    {
        UnitId = value.EinheitId,
        OwnerId = value.EigentuemerId,
        PercentageText = value.AnteilProzent.ToString("N4", SwissCulture)
    };
}
