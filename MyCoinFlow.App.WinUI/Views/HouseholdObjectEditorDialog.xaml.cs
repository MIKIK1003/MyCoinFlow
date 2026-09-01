using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;

namespace MyCoinFlow.WinUI.Views;
public sealed partial class HouseholdObjectEditorDialog : ContentDialog
{
    private readonly HaushaltObjekt? _source;
    public HouseholdObjectEditorDialog(int? roomId, HaushaltObjekt? source = null)
    {
        InitializeComponent(); _source = source; var db = new DatabaseService(); var rooms = db.HaushaltRaeumeGetAll(); RoomBox.ItemsSource = rooms; CategoryBox.ItemsSource = db.HaushaltObjektKategorienGetAll(); InstructionBox.ItemsSource = db.HaushaltArbeitsanweisungenGetAll(); IntervalBox.ItemsSource = db.HaushaltZeitintervalleGetAll(); NameBox.Text = source?.Bezeichnung ?? string.Empty; RoomBox.SelectedItem = rooms.FirstOrDefault(value => value.Id == (source?.RaumId ?? roomId)) ?? rooms.FirstOrDefault(); CategoryBox.SelectedValuePath = "Id"; CategoryBox.SelectedValue = source?.KategorieId; InstructionBox.SelectedValuePath = "Id"; InstructionBox.SelectedValue = source?.ArbeitsanweisungId; IntervalBox.SelectedValuePath = "Id"; IntervalBox.SelectedValue = source?.ZeitintervallId; LeadDaysBox.Value = source?.VorlaufTage ?? 0; ManufacturerBox.Text = source?.Hersteller ?? string.Empty; ModelBox.Text = source?.Modell ?? string.Empty; SerialBox.Text = source?.Seriennummer ?? string.Empty; PurchaseDatePicker.Date = source?.Kaufdatum; LastDonePicker.Date = source?.LetzteAusfuehrungAm; PriceBox.Text = source?.Kaufpreis?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty; NoteBox.Text = source?.Bemerkung ?? string.Empty;
    }
    public HaushaltObjekt? Result { get; private set; }
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || RoomBox.SelectedItem is not HaushaltRaum room) { Error(args, "Bitte Bezeichnung und Raum erfassen."); return; } if (CategoryBox.SelectedItem is not HaushaltObjektKategorie category) { Error(args, "Bitte eine Objekt-Kategorie auswählen."); return; } if (InstructionBox.SelectedItem is not HaushaltArbeitsanweisung instruction) { Error(args, "Bitte eine Tätigkeit auswählen."); return; } if (IntervalBox.SelectedItem is not HaushaltZeitintervall interval) { Error(args, "Bitte ein Zeitintervall auswählen."); return; }
        decimal? price = null; if (!string.IsNullOrWhiteSpace(PriceBox.Text)) { if (!decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)) { Error(args, "Bitte beim Kaufpreis eine gültige Zahl erfassen."); return; } price = parsed; }
        Result = new HaushaltObjekt { Id = _source?.Id ?? 0, RaumId = room.Id, RaumBezeichnung = room.Bezeichnung, Bezeichnung = NameBox.Text.Trim(), Kategorie = category.Bezeichnung, KategorieId = category.Id, KategorieBezeichnung = category.Bezeichnung, KategorieIconKey = category.IconKey, IconKey = category.IconKey, ArbeitsanweisungId = instruction.Id, ArbeitsanweisungBezeichnung = instruction.Bezeichnung, ArbeitsanweisungBeschreibung = instruction.Beschreibung, ZeitintervallId = interval.Id, ZeitintervallBezeichnung = interval.Bezeichnung, ZeitintervallTage = interval.Tage, VorlaufTage = (int)LeadDaysBox.Value, Hersteller = ManufacturerBox.Text.Trim(), Modell = ModelBox.Text.Trim(), Seriennummer = SerialBox.Text.Trim(), Kaufdatum = PurchaseDatePicker.Date?.Date, Kaufpreis = price, LetzteAusfuehrungAm = LastDonePicker.Date?.Date, Bemerkung = NoteBox.Text.Trim(), IstAktiv = true, ErstelltAm = _source?.ErstelltAm ?? default, GeaendertAm = _source?.GeaendertAm };
    }
    private void Error(ContentDialogButtonClickEventArgs args, string message) { args.Cancel = true; ErrorBar.Message = message; ErrorBar.IsOpen = true; }
}
