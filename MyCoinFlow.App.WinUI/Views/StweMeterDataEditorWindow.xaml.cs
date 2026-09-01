using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class StweMeterDataEditorWindow : PersistentWindow
{
    private readonly DatabaseService _database = new();
    private readonly StweZaehlerdatenSet _model = new();
    private readonly ObservableCollection<StweMeterReadingEditorRow> _rows = new();
    private bool _ready;
    public event EventHandler<int>? Saved;

    public StweMeterDataEditorWindow(int propertyId, StweZaehlerdatenSet? existing = null)
    {
        InitializeComponent(); AppWindow.Resize(new SizeInt32(1180, 780));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = 920; presenter.PreferredMinimumHeight = 620; }
        _model.LiegenschaftId = propertyId;
        if (existing is not null)
        {
            _model.Id = existing.Id; _model.LiegenschaftId = existing.LiegenschaftId; _model.ErfasstAm = existing.ErfasstAm; _model.RechnungKwhTotal = existing.RechnungKwhTotal; _model.GutschriftChf = existing.GutschriftChf; _model.RueckgespeistKwh = existing.RueckgespeistKwh; _model.Notiz = existing.Notiz; _model.ErfassungsTyp = existing.ErfassungsTyp; _model.MonatsAnzahl = existing.MonatsAnzahl;
        }
        else { _model.ErfasstAm = DateTime.Today; _model.ErfassungsTyp = 0; }
        HeadingText.Text = _model.Id > 0 ? "Zählerdaten bearbeiten" : "Zählerdaten neu";
        DatePicker.Date = _model.ErfasstAm; InvoiceBox.Text = Format(_model.RechnungKwhTotal, "0.###"); CreditBox.Text = Format(_model.GutschriftChf, "0.00"); FeedInBox.Text = Format(_model.RueckgespeistKwh, "0.###"); NoteBox.Text = _model.Notiz ?? string.Empty;
        LoadRows(); RowsList.ItemsSource = _rows;
        ModeBox.SelectedIndex = _model.ErfassungsTyp == 1 ? 1 : 0; MonthsBox.Value = _model.MonatsAnzahl ?? 1;
        EnsureMonthSlots(); LoadExistingMonths(); _ready = true; ApplyMode();
    }

    private void LoadRows()
    {
        Dictionary<int, decimal> values = new();
        if (_model.Id > 0) values = _database.StweZaehlerdatenLinesGetBySet(_model.Id).ToDictionary(value => value.ZaehlerId, value => value.NeuWert);
        else
        {
            var latest = _database.StweZaehlerdatenSetsGetByLiegenschaft(_model.LiegenschaftId).FirstOrDefault();
            if (latest is not null) values = _database.StweZaehlerdatenLinesGetBySet(latest.Id).ToDictionary(value => value.ZaehlerId, value => value.NeuWert);
        }
        foreach (var meter in _database.StweZaehlerGetByLiegenschaft(_model.LiegenschaftId))
        {
            values.TryGetValue(meter.Id, out var value);
            _rows.Add(new StweMeterReadingEditorRow { MeterId = meter.Id, Type = meter.Typ, Name = meter.Name, UnitId = meter.EinheitId, NewText = value > 0m ? value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty });
        }
    }
    private void LoadExistingMonths()
    {
        if (_model.Id <= 0 || _model.ErfassungsTyp != 1) return;
        var months = _database.StweZaehlerdatenMonateGetBySet(_model.Id);
        foreach (var row in _rows)
        {
            var values = months.Where(value => value.ZaehlerId == row.MeterId).OrderBy(value => value.MonatIndex).ToList();
            if (values.Count == 0) continue;
            row.EnsureMonthSlots(values.Max(value => value.MonatIndex));
            foreach (var value in values) { var slot = row.Months.FirstOrDefault(month => month.MonthIndex == value.MonatIndex); if (slot is not null) slot.Text = value.Kwh.ToString("0.###", CultureInfo.InvariantCulture); }
        }
    }
    private void OnModeChanged(object sender, SelectionChangedEventArgs e) { if (_ready) ApplyMode(); }
    private void ApplyMode()
    {
        var monthly = ModeBox.SelectedIndex == 1; MonthsBox.IsEnabled = monthly;
        foreach (var row in _rows) row.IsMonthly = monthly;
        if (monthly) EnsureMonthSlots();
    }
    private void OnMonthsChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { if (_ready && ModeBox.SelectedIndex == 1) EnsureMonthSlots(); }
    private void EnsureMonthSlots() { var count = double.IsNaN(MonthsBox.Value) ? 0 : Math.Max(0, (int)MonthsBox.Value); foreach (var row in _rows) row.EnsureMonthSlots(count); }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var error = ValidateAndCopy(); if (error is not null) { ShowError(error); return; }
        try
        {
            var id = _model.Id > 0 ? _model.Id : _database.StweZaehlerdatenSetInsert(_model);
            if (_model.Id > 0) _database.StweZaehlerdatenSetUpdate(_model);
            if (_model.ErfassungsTyp == 1)
            {
                var months = _rows.SelectMany(row => row.Months.Select(month => (row.MeterId, month.MonthIndex, month.Kwh))).ToList();
                var sums = _rows.Select(row => (row.MeterId, row.MonthSum)).ToList();
                _database.StweZaehlerdatenMonateReplace(id, months); _database.StweZaehlerdatenLinesReplace(id, sums);
            }
            else
            {
                _database.StweZaehlerdatenLinesReplace(id, _rows.Select(row => (row.MeterId, row.NewValue)).ToList());
                _database.StweZaehlerdatenMonateReplace(id, new());
            }
            Saved?.Invoke(this, id); Close();
        }
        catch (Exception exception) { ShowError("Speichern fehlgeschlagen: " + exception.Message); }
        await Task.CompletedTask;
    }
    private string? ValidateAndCopy()
    {
        if (_model.LiegenschaftId <= 0) return "Liegenschaft fehlt.";
        if (DatePicker.Date is null) return "Bitte ein Erfassungsdatum wählen.";
        if (_rows.Count == 0) return "Keine Zähler vorhanden. Bitte zuerst Zähler unter „Liegenschaften → Zähler“ erfassen.";
        _model.ErfasstAm = DatePicker.Date.Value.Date; _model.ErfassungsTyp = ModeBox.SelectedIndex == 1 ? 1 : 0; _model.MonatsAnzahl = _model.ErfassungsTyp == 1 ? (int)MonthsBox.Value : null;
        _model.RechnungKwhTotal = ParseNullable(InvoiceBox.Text); _model.GutschriftChf = ParseNullable(CreditBox.Text); _model.RueckgespeistKwh = ParseNullable(FeedInBox.Text); _model.Notiz = NoteBox.Text;
        if (_model.ErfassungsTyp == 1)
        {
            if (_model.MonatsAnzahl is null or <= 0) return "Bitte Anzahl Monate > 0 erfassen.";
            if (_rows.Any(row => row.Months.Count == 0 || row.Months.Any(month => string.IsNullOrWhiteSpace(month.Text)))) return "Bitte bei allen Zählern alle Monatswerte erfassen.";
        }
        else if (_rows.Any(row => string.IsNullOrWhiteSpace(row.NewText))) return "Bitte bei allen Zählern einen Neu-Wert erfassen.";
        return null;
    }
    private static decimal? ParseNullable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim().Replace("’", "'").Replace("'", string.Empty).Replace(" ", string.Empty).Replace(",", ".");
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
    private static string Format(decimal? value, string pattern) => value?.ToString(pattern, CultureInfo.InvariantCulture) ?? string.Empty;
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    private void ShowError(string message) { StatusBar.Message = message; StatusBar.IsOpen = true; }
}
