using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.ViewModels;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingPage : Page
{
    private readonly InvoicingViewModel _viewModel = new();

    public InvoicingPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsLoading) return;
        await ReloadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnRefreshShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ReloadAsync();
    }

    private void OnSearchShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SearchBox.IsEnabled)
            SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnFinanceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Overview?.IsAdmin != true) return;
        (Application.Current as App)?.MainWindow.NavigateToFinanceSettings();
    }

    private async Task ReloadAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        await _viewModel.LoadAsync();
        Render();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void Render()
    {
        var overview = _viewModel.Overview;
        if (overview is null)
        {
            ErrorInfoBar.Message = _viewModel.ErrorMessage ?? "Unbekannter Fehler.";
            ErrorInfoBar.IsOpen = true;
            ContextBadgeText.Text = "Nicht verbunden";
            ConfigurationMetricText.Text = "Fehler";
            CurrencyMetricText.Text = "Basiswährung —";
            EmptyStateTitle.Text = "Fakturieren ist momentan nicht verfügbar";
            EmptyStateDescription.Text =
                "Die Mandantendatenbank oder das Fakturierungsschema konnte nicht geladen werden. " +
                "Prüfen Sie die Verbindung und versuchen Sie es erneut.";
            EmptyStateSettingsButton.Visibility = Visibility.Collapsed;
            SettingsActionGroup.Visibility = Visibility.Collapsed;
            SettingsSeparator.Visibility = Visibility.Collapsed;
            StatusText.Text = "Ladefehler · Keine Daten wurden verändert.";
            SchemaText.Text = "Schema nicht geprüft";
            IssuerText.Text = CurrenciesText.Text = VatText.Text = PaymentText.Text = "—";
            MissingConfigurationText.Text = string.Empty;
            return;
        }

        ContextBadgeText.Text = $"{overview.DatabaseName} · {overview.BaseCurrency}";
        SettingsActionGroup.Visibility = overview.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        SettingsSeparator.Visibility = SettingsActionGroup.Visibility;
        ConfigurationMetricText.Text = overview.IsConfigured ? "Bereit" : "Unvollständig";
        ConfigurationMetricText.Foreground = overview.IsConfigured
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MyCoinFlowPositiveBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MyCoinFlowDangerBrush"];
        CurrencyMetricText.Text =
            $"Basis {overview.BaseCurrency} · {overview.ActiveCurrencyCount} aktiv";

        if (overview.IsConfigured)
        {
            EmptyStateTitle.Text = "Noch keine Fakturierungsvorgänge";
            EmptyStateDescription.Text =
                "Die Finanzgrundlage ist vollständig eingerichtet. Die Vorgangserfassung wird " +
                "mit dem nächsten Fakturieren-Ausbauschritt freigeschaltet.";
            EmptyStateSettingsButton.Visibility = Visibility.Collapsed;
            MissingConfigurationText.Text = string.Empty;
        }
        else
        {
            EmptyStateTitle.Text = "Finanzgrundlage vervollständigen";
            EmptyStateDescription.Text = overview.IsAdmin
                ? "Erfassen Sie die fehlenden Finanzstammdaten, bevor Dokumente angelegt werden."
                : "Die Finanzstammdaten sind noch nicht vollständig. Bitte wenden Sie sich an eine Administratorin oder einen Administrator.";
            EmptyStateSettingsButton.Visibility = overview.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            MissingConfigurationText.Text =
                "Fehlt: " + string.Join(", ", overview.MissingConfiguration) + ".";
        }

        IssuerText.Text = string.IsNullOrWhiteSpace(overview.IssuerName)
            ? "Noch nicht eingerichtet"
            : overview.IssuerName;
        CurrenciesText.Text = $"{overview.ActiveCurrencyCount} aktiv · Basis {overview.BaseCurrency}";
        VatText.Text = overview.ActiveVatRateCount == 1
            ? "1 aktiver Satz"
            : $"{overview.ActiveVatRateCount} aktive Sätze";
        PaymentText.Text =
            $"{overview.ActivePaymentAccountCount} Zahlungskonto/-konten · " +
            $"{overview.RevenueAccountCount} Ertragskonto/-konten";
        StatusText.Text =
            $"Mandant: {overview.DatabaseName} · Angemeldete Fakturierungsarbeit freigegeben" +
            (overview.IsAdmin ? " · Finanzstammdaten administrierbar" : " · Finanzstammdaten schreibgeschützt");
        SchemaText.Text = $"Schema v{overview.SchemaVersion}";
    }
}
