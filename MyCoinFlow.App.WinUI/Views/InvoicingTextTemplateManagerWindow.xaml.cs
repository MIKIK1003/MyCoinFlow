using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingTextTemplateManagerWindow : PersistentWindow
{
    private readonly InvoicingPositionRepository _repository;
    private IReadOnlyList<InvoicingTextTemplateRecord> _templates = [];
    private InvoicingTextTemplateDraft _draft = new();
    private InvoicingFormattedTextSnapshot _textSnapshot = new(string.Empty, null);
    private bool _loaded;
    private bool _saving;

    public InvoicingTextTemplateManagerWindow(InvoicingPositionRepository? repository = null)
    {
        InitializeComponent();
        _repository = repository ?? new InvoicingPositionRepository();
        ConfigureDpiAwareSizing(RootGrid, 1120, 780, 720, 580);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
        Closed += OnWindowClosed;
    }

    public bool Changed { get; private set; }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 860;
        ContentGrid.ColumnDefinitions[0].Width = wide
            ? new GridLength(4, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(6, GridUnitType.Star)
            : new GridLength(0);
        ContentGrid.RowDefinitions[0].Height = wide
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        ContentGrid.RowDefinitions[1].Height = wide
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumnSpan(ListCard, wide ? 1 : 2);
        Grid.SetRow(EditorCard, wide ? 0 : 1);
        Grid.SetColumn(EditorCard, wide ? 1 : 0);
        Grid.SetColumnSpan(EditorCard, wide ? 1 : 2);
        ListCard.MaxHeight = wide ? double.PositiveInfinity : 240;
    }

    private async Task LoadAsync(int? selectedId = null)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        try
        {
            _templates = await _repository.LoadTextTemplatesAsync(includeInactive: true);
            TemplatesList.ItemsSource = _templates;
            EmptyState.Visibility = _templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TemplatesList.Visibility = _templates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var selected = selectedId.HasValue
                ? _templates.FirstOrDefault(item => item.Id == selectedId.Value)
                : _templates.FirstOrDefault();
            TemplatesList.SelectedItem = selected;
            if (selected is null)
                BeginNew();
            else
                LoadDraft(selected);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Textbausteine konnten nicht geladen werden.";
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplatesList.SelectedItem is InvoicingTextTemplateRecord template)
            LoadDraft(template);
    }

    private void LoadDraft(InvoicingTextTemplateRecord template)
    {
        _draft = template.ToDraft();
        _textSnapshot = new InvoicingFormattedTextSnapshot(
            template.PlainText,
            template.FormattedText);
        EditorTitle.Text = $"«{template.Name}» bearbeiten";
        NameBox.Text = template.Name;
        PreviewBox.Text = template.PlainText;
        ActiveToggle.IsOn = template.IsActive;
        SaveButton.IsEnabled = true;
        StatusText.Text = template.IsActive
            ? "Aktiver Textbaustein · Änderungen werden erst mit Speichern wirksam."
            : "Inaktiver Textbaustein · bleibt historisch erhalten und wird nicht angeboten.";
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => BeginNew();

    private void OnNewShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        BeginNew();
    }

    private void BeginNew()
    {
        TemplatesList.SelectedItem = null;
        _draft = new InvoicingTextTemplateDraft();
        _textSnapshot = new InvoicingFormattedTextSnapshot(string.Empty, null);
        EditorTitle.Text = "Neuen Textbaustein erfassen";
        NameBox.Text = string.Empty;
        PreviewBox.Text = string.Empty;
        ActiveToggle.IsOn = true;
        SaveButton.IsEnabled = true;
        StatusText.Text = "Bezeichnung erfassen und Text mit F2 oder «Text im Editor öffnen» bearbeiten.";
        NameBox.Focus(FocusState.Programmatic);
    }

    private async void OnEditTextClick(object sender, RoutedEventArgs e) => await EditTextAsync();

    private async void OnEditTextShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await EditTextAsync();
    }

    private async Task EditTextAsync()
    {
        ErrorInfoBar.IsOpen = false;
        var editor = new InvoicingFormattedTextEditorWindow(
            _textSnapshot,
            heading: string.IsNullOrWhiteSpace(NameBox.Text)
                ? "Textbaustein bearbeiten"
                : $"Textbaustein «{NameBox.Text.Trim()}»",
            description: "Text und Formatierung werden als geprüftes Paar in den Baustein übernommen.");
        if (!await editor.ShowAsync() || editor.Snapshot is null) return;
        _textSnapshot = editor.Snapshot;
        PreviewBox.Text = _textSnapshot.PlainText;
        StatusText.Text = "Text übernommen · Textbaustein noch speichern.";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        ErrorInfoBar.IsOpen = false;
        try
        {
            _draft.Name = NameBox.Text;
            _draft.PlainText = _textSnapshot.PlainText;
            _draft.FormattedText = _textSnapshot.FormattedText;
            _draft.IsActive = ActiveToggle.IsOn;
            var id = await _repository.SaveTextTemplateAsync(_draft);
            Changed = true;
            await LoadAsync(id);
            StatusText.Text = "Textbaustein gespeichert.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            StatusText.Text = "Textbaustein wurde nicht gespeichert.";
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCloseShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        RootGrid.SizeChanged -= OnRootSizeChanged;
        Activated -= OnActivated;
        Closed -= OnWindowClosed;
    }
}
