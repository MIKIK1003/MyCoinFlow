using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MyCoinFlow.WinUI.Views;
public enum HouseholdMasterKind { Location, Room, Category, Instruction, Interval }
public sealed partial class HouseholdMasterEditorDialog : ContentDialog
{
    private static readonly string[] Icons = { "HomeCityOutline", "HomeOutline", "OfficeBuildingOutline", "Warehouse", "Car", "Tools", "Factory", "CabinAFrame", "Garage", "CogOutline", "FloorPlan", "SilverwareForkKnife", "Shower", "SofaOutline", "BedOutline", "WashingMachine", "PackageVariantClosed", "ClipboardTextOutline", "CalendarClock" };
    public HouseholdMasterEditorDialog(HouseholdMasterKind kind, string name = "", string icon = "", string color = "", int days = 0, string description = "")
    {
        InitializeComponent(); Kind = kind; IconBox.ItemsSource = Icons; ColorBox.ItemsSource = new[] { "DeepPurple", "Blue", "Teal", "Green", "Amber", "Orange", "Red", "BlueGrey" }; NameBox.Text = name; IconBox.SelectedItem = string.IsNullOrWhiteSpace(icon) ? DefaultIcon(kind) : icon; ColorBox.SelectedItem = string.IsNullOrWhiteSpace(color) ? "DeepPurple" : color; DaysBox.Value = days > 0 ? days : 1; DescriptionBox.Text = description;
        Title = kind switch { HouseholdMasterKind.Location => "Standort", HouseholdMasterKind.Room => "Raum", HouseholdMasterKind.Category => "Objekt-Kategorie", HouseholdMasterKind.Instruction => "Tätigkeit", _ => "Zeitintervall" };
        ColorBox.Visibility = kind == HouseholdMasterKind.Location ? Visibility.Visible : Visibility.Collapsed; DaysBox.Visibility = kind == HouseholdMasterKind.Interval ? Visibility.Visible : Visibility.Collapsed; IconBox.Visibility = kind == HouseholdMasterKind.Interval ? Visibility.Collapsed : Visibility.Visible; DescriptionBox.Header = kind == HouseholdMasterKind.Instruction ? "Beschreibung" : "Bemerkung";
    }
    public HouseholdMasterKind Kind { get; } public string ValueName => NameBox.Text.Trim(); public string Icon => IconBox.SelectedItem as string ?? DefaultIcon(Kind); public string Color => ColorBox.SelectedItem as string ?? "DeepPurple"; public int Days => (int)DaysBox.Value; public string Description => DescriptionBox.Text.Trim();
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) { if (!string.IsNullOrWhiteSpace(ValueName) && (Kind != HouseholdMasterKind.Interval || Days > 0)) return; args.Cancel = true; ErrorBar.Message = Kind == HouseholdMasterKind.Interval ? "Bitte Bezeichnung und Tage > 0 erfassen." : "Bitte eine Bezeichnung erfassen."; ErrorBar.IsOpen = true; }
    private static string DefaultIcon(HouseholdMasterKind kind) => kind switch { HouseholdMasterKind.Location => "HomeCityOutline", HouseholdMasterKind.Room => "HomeOutline", HouseholdMasterKind.Category => "PackageVariantClosed", HouseholdMasterKind.Instruction => "ClipboardTextOutline", _ => "CalendarClock" };
}
