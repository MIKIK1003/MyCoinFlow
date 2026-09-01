using Microsoft.UI.Xaml.Media;
using MyCoinFlow.Models;

namespace MyCoinFlow.WinUI.Models;

public sealed class HouseholdTaskTile
{
    private static readonly Brush TextColor = Brush(255, 37, 37, 37);
    private static readonly Brush OverdueBackground = Brush(255, 252, 232, 230);
    private static readonly Brush OverdueAccent = Brush(255, 164, 38, 44);
    private static readonly Brush DueSoonBackground = Brush(255, 255, 244, 206);
    private static readonly Brush DueSoonAccent = Brush(255, 157, 93, 0);
    private static readonly Brush ActiveBackground = Brush(255, 234, 244, 227);
    private static readonly Brush ActiveAccent = Brush(255, 73, 130, 5);

    public HouseholdTaskTile(HaushaltAufgabe value) => Value = value;

    public HaushaltAufgabe Value { get; }
    public int Id => Value.Id;
    public string Title => Value.Titel;
    public string ObjectName => Value.ObjektBezeichnung;
    public string ActivationDateText => Value.AktivAb.ToString("dd.MM.yyyy");
    public string DueDateText => Value.FaelligAm.ToString("dd.MM.yyyy");
    public int RemainingDays => (Value.FaelligAm.Date - DateTime.Today).Days;
    public bool IsOverdue => DateTime.Today > Value.FaelligAm.Date;
    public bool IsDueSoon => !IsOverdue && RemainingDays <= 2;
    public string StatusText => IsOverdue ? "Überfällig" : IsDueSoon ? "Bald fällig" : "Aktiv";
    public string RemainingText => IsOverdue
        ? $"Überfällig seit {Math.Abs(RemainingDays)} Tag(en)"
        : RemainingDays == 0
            ? "Heute fällig"
            : RemainingDays == 1
                ? "Morgen fällig"
                : $"Noch {RemainingDays} Tage";
    public Brush TileBrush => IsOverdue ? OverdueBackground : IsDueSoon ? DueSoonBackground : ActiveBackground;
    public Brush AccentBrush => IsOverdue ? OverdueAccent : IsDueSoon ? DueSoonAccent : ActiveAccent;
    public Brush TextBrush => TextColor;

    private static SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new(Microsoft.UI.ColorHelper.FromArgb(alpha, red, green, blue));
}
