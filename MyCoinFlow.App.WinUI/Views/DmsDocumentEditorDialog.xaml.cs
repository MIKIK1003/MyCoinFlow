using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Services;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class DmsDocumentEditorDialog : PersistentWindow
{
    private readonly DatabaseService _database = new();
    private readonly DmsDocument? _source;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private bool _categoryDeleteArmed;

    public DmsDocumentEditorDialog(DmsDocument? source = null)
    {
        InitializeComponent();
        _source = source;
        Title = source is null ? "Dokument hochladen" : "Dokument organisieren";
        WindowHeadingText.Text = Title;
        AppWindow.Resize(new SizeInt32(1460, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1240;
            presenter.PreferredMinimumHeight = 780;
        }
        Closed += OnWindowClosed;

        FilePathBox.Text = source?.FileName ?? string.Empty;
        SelectFileButton.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        var categories = _database.GetDistinctKategorien();
        CategoryBox.ItemsSource = categories;
        DocumentTypeBox.ItemsSource = Enum.GetValues<DmsBelegart>();
        WorkflowStatusBox.ItemsSource = Enum.GetValues<DmsBearbeitungsstatus>();
        var addresses = new List<Adresse> { new() { Id = 0, Name = "(keine Adresse)" } };
        addresses.AddRange(_database.LadeAdressen().OrderBy(value => value.Name));
        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedValue = source?.AdresseId ?? 0;
        TitleBox.Text = source?.Titel ?? string.Empty;
        TaxDocumentBox.IsChecked = source?.IstSteuerunterlage == true;
        SetCategory(source?.Kategorie, categories);
        DocumentTypeBox.SelectedItem = source?.Belegart;
        KeywordsBox.Text = source?.Schlagwoerter ?? string.Empty;
        DescriptionBox.Text = source?.Beschreibung ?? string.Empty;
        DocumentDatePicker.Date = source is null ? DateTime.Today : source.DokumentDatum;
        var amount = source?.TransBetrag ?? source?.ErkannterBetrag;
        AmountBox.Text = amount?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty;
        AmountBox.IsReadOnly = source?.EntityType is not null;
        WarrantyBox.IsChecked = source?.IstGarantieschein == true;
        WarrantyDatePicker.Date = source?.GarantieAblaufDatum;
        WorkflowStatusBox.SelectedItem = source?.Bearbeitungsstatus ?? DmsBearbeitungsstatus.Neu;
        ResponsibleBox.Text = source?.Verantwortlich ?? string.Empty;
        DueActiveBox.IsChecked = source?.ExplizitFaelligAm.HasValue == true;
        DueDatePicker.Date = source?.ExplizitFaelligAm ?? DateTime.Today.AddDays(source is null ? 30 : 7);
        RetainActiveBox.IsChecked = source?.AufbewahrenBis.HasValue == true;
        RetainDatePicker.Date = source?.AufbewahrenBis ?? DateTime.Today.AddYears(10);
        NoteBox.Text = source?.Notiz ?? string.Empty;
        LinkInfoText.Text = source?.EntityType is not null
            ? $"Verknüpft mit {source.VerknuepftMitAnzeige}" +
              (string.IsNullOrWhiteSpace(source.TransAdresseName) ? string.Empty : $" · {source.TransAdresseName}") +
              ". Betrag stammt aus der Buchung und wird dort gepflegt; die Verknüpfung selbst über „Transaktion zuweisen“ ändern. Ohne eigene Adresse wird die Adresse der Buchung angezeigt."
            : source is null
                ? "Nach dem Hochladen kann das Dokument über „Transaktion zuweisen“ mit einer Buchung verknüpft werden."
                : "Noch keiner Transaktion zugeordnet. Der Betrag stammt aus der Texterkennung und kann hier korrigiert werden; dies verbessert das automatische Matching.";
        UpdateVisibility();
    }

    public string? SelectedFilePath { get; private set; }
    public DmsDocumentChanges? Changes { get; private set; }
    public bool IsTaxDocument => TaxDocumentBox.IsChecked == true;

    public Task<bool> ShowAsync()
    {
        Activate();
        return _completion.Task;
    }

    private async void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        SelectedFilePath = await FilePickerService.PickOpenAsync(this, ".pdf", ".jpg", ".jpeg", ".png", "*");
        FilePathBox.Text = SelectedFilePath ?? string.Empty;
    }

    private void OnWarrantyChanged(object sender, RoutedEventArgs e) => UpdateVisibility();
    private void OnDueChanged(object sender, RoutedEventArgs e) => UpdateVisibility();
    private void OnRetainChanged(object sender, RoutedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (WarrantyDatePicker is null) return;
        WarrantyDatePicker.Visibility = WarrantyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DueDatePicker.Visibility = DueActiveBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RetainDatePicker.Visibility = RetainActiveBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        var category = GetCategoryText();
        if (string.IsNullOrWhiteSpace(category))
        {
            ShowInline("Es ist keine Kategorie ausgewählt.", InfoBarSeverity.Informational);
            return;
        }

        var count = _database.ZaehleDokumenteMitKategorie(category);
        if (!_categoryDeleteArmed)
        {
            _categoryDeleteArmed = true;
            DeleteCategoryButton.Content = count == 0 ? "Löschen bestätigen" : $"Bestätigen ({count})";
            ShowInline("Erneut klicken, um die Kategorie zu entfernen. Die Dokumente bleiben erhalten und haben danach keine Kategorie mehr.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            _database.LoescheKategorie(category);
            CategoryBox.ItemsSource = _database.GetDistinctKategorien();
            CategoryBox.SelectedItem = null;
            CategoryBox.Text = string.Empty;
            _categoryDeleteArmed = false;
            DeleteCategoryButton.Content = "Löschen";
            ShowInline("Kategorie entfernt.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowInline("Kategorie konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_source is null && string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            ShowInline("Bitte zuerst eine Datei auswählen.", InfoBarSeverity.Warning);
            return;
        }
        if (GetCategoryText().Length > 100)
        {
            ShowInline("Kategorie darf höchstens 100 Zeichen enthalten.", InfoBarSeverity.Warning);
            CategoryBox.Focus(FocusState.Programmatic);
            return;
        }
        if (DueActiveBox.IsChecked == true && !DueDatePicker.Date.HasValue)
        {
            ShowInline("Bitte für die Fälligkeit ein Datum auswählen.", InfoBarSeverity.Warning);
            DueDatePicker.Focus(FocusState.Programmatic);
            return;
        }
        if (RetainActiveBox.IsChecked == true && !RetainDatePicker.Date.HasValue)
        {
            ShowInline("Bitte für die Aufbewahrungsfrist ein Datum auswählen.", InfoBarSeverity.Warning);
            RetainDatePicker.Focus(FocusState.Programmatic);
            return;
        }

        decimal? amount = null;
        var amountText = (AmountBox.Text ?? string.Empty).Trim().Replace("'", string.Empty);
        if (!string.IsNullOrWhiteSpace(amountText))
        {
            if (!(decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
                  || decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)))
            {
                ShowInline("Der Betrag konnte nicht gelesen werden. Bitte z. B. 84.60 eingeben.", InfoBarSeverity.Warning);
                AmountBox.Focus(FocusState.Programmatic);
                return;
            }
            amount = parsed;
        }

        var addressId = AddressBox.SelectedValue is int id && id > 0 ? id : (int?)null;
        Changes = new DmsDocumentChanges(
            NullIfEmpty(TitleBox.Text),
            NullIfEmpty(GetCategoryText()),
            DocumentTypeBox.SelectedItem as DmsBelegart?,
            NullIfEmpty(DescriptionBox.Text),
            NullIfEmpty(KeywordsBox.Text),
            NullIfEmpty(NoteBox.Text),
            WorkflowStatusBox.SelectedItem is DmsBearbeitungsstatus status ? status : DmsBearbeitungsstatus.Neu,
            NullIfEmpty(ResponsibleBox.Text),
            DocumentDatePicker.Date?.Date,
            amount,
            addressId,
            WarrantyBox.IsChecked == true,
            WarrantyBox.IsChecked == true ? WarrantyDatePicker.Date?.Date : null,
            DueActiveBox.IsChecked == true ? DueDatePicker.Date?.Date : null,
            RetainActiveBox.IsChecked == true ? RetainDatePicker.Date?.Date : null);
        Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void Complete(bool saved)
    {
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(saved);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(false);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SetCategory(string? category, IReadOnlyList<string> categories)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        var existing = categories.FirstOrDefault(value =>
            string.Equals(value, category, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
            CategoryBox.SelectedItem = existing;
        else
            CategoryBox.Text = category.Trim();
    }

    private string GetCategoryText()
    {
        var text = (CategoryBox.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(text)) return text;
        return (CategoryBox.SelectedItem as string ?? string.Empty).Trim();
    }

    private void ShowInline(string message, InfoBarSeverity severity)
    {
        InlineInfo.Message = message;
        InlineInfo.Severity = severity;
        InlineInfo.IsOpen = true;
    }
}
