using System.Collections.ObjectModel;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Models;

public sealed class DmsDisplayRow
{
    public DmsDisplayRow(DmsDocument value, string group)
    {
        Value = value;
        Group = group;
    }

    public DmsDocument Value { get; }
    public int Id => Value.Id;
    public string Group { get; }
    public string Favorite => Value.FavoritSymbol;
    public string TaxMarker => Value.IstSteuerunterlage ? "STEUERN" : string.Empty;
    public string Title => Value.TitelAnzeige;
    public string Keywords => Value.SchlagwoerterAnzeige;
    public string Duplicate => Value.DuplikatAnzeige;
    public string Date => (Value.DokumentDatum ?? Value.ImportedAtUtc).ToString("dd.MM.yyyy");
    public string Version => Value.VersionAnzeige;
    public string Status => Value.BearbeitungsstatusAnzeige;
    public string Responsible => Value.VerantwortlichAnzeige;
    public string FileType => Path.GetExtension(Value.FileName).TrimStart('.').ToUpperInvariant() is { Length: > 0 } type
        ? type
        : "DATEI";
    public string Size => Value.SizeDisplay;
    public string LinkStatus => Value.EntityType is null ? "Frei" : "Verknüpft";
}

public sealed class DmsDisplayGroup
{
    public DmsDisplayGroup(string key, IEnumerable<DmsDisplayRow> entries)
    {
        Key = key;
        Entries = new ObservableCollection<DmsDisplayRow>(entries);
    }

    public string Key { get; }
    public string Title => Key;
    public ObservableCollection<DmsDisplayRow> Entries { get; }
    public int Count => Entries.Count;
    public int OpenCount => Entries.Count(entry => entry.Value.Bearbeitungsstatus != DmsBearbeitungsstatus.Erledigt);
    public string Summary => OpenCount == 0
        ? $"{Count} Dokumente · vollständig bearbeitet"
        : $"{Count} Dokumente · {OpenCount} offen";
}
