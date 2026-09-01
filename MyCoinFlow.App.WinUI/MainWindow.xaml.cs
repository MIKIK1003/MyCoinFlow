using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Views;
using MyCoinFlow.Services;
using MyCoinFlow.Models;
using System.ComponentModel;
using Windows.Graphics;

namespace MyCoinFlow.WinUI;

public sealed partial class MainWindow : PersistentWindow
{
    private LoginSession? _session;
    private bool _suppressNavigationSelection;
    private DmsTransactionWindow? _watcherAssignmentWindow;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1360, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1040;
            presenter.PreferredMinimumHeight = 700;
        }

        LoginPage.LoginSucceeded += OnLoginSucceeded;
        RootGrid.Loaded += async (_, _) => await LoginPage.InitializeAsync();
        AppNavigation.TransaktionAnzeigen += OnTransactionNavigationRequested;
        Closed += (_, _) =>
        {
            AppNavigation.TransaktionAnzeigen -= OnTransactionNavigationRequested;
            try { DmsWatcherService.Instance.Stop(); } catch { }
            DisconnectDmsUiBridge();
        };
    }

    private void OnLoginSucceeded(object? sender, LoginSession session)
    {
        _session = session;
        MyCoinFlow.Services.ConnectionStrings.SetActiveDatabase(session.DatabaseName);
        MyCoinFlow.Services.CurrentUserContext.SignIn(session.Username, session.IsAdmin);
        ApplyModuleAccess();
        ConnectDmsUiBridge();
        try { DmsWatcherService.Instance.Start(); } catch { }
        DatabaseText.Text = $"Datenbank: {session.DatabaseName}";
        UserText.Text = session.IsAdmin ? $"{session.Username} · Admin" : session.Username;
        LoginRoot.Visibility = Visibility.Collapsed;
        ShellRoot.Visibility = Visibility.Visible;
        SessionPanel.Visibility = Visibility.Visible;
        RootNavigationView.SelectedItem = DashboardNavItem;
        Navigate("Dashboard");
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Abmelden?",
            Content = $"Die Sitzung von {_session?.Username} wird beendet.",
            PrimaryButtonText = "Abmelden",
            CloseButtonText = "Zurück",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        _session = null;
        DisconnectDmsUiBridge();
        try { DmsWatcherService.Instance.Stop(); } catch { }
        MyCoinFlow.Services.CurrentUserContext.SignOut();
        ContentFrame.Content = null;
        RootNavigationView.SelectedItem = null;
        ShellRoot.Visibility = Visibility.Collapsed;
        SessionPanel.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Visible;
        await LoginPage.ResetAsync();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigationSelection) return;
        if (args.SelectedItemContainer?.Tag is string tag) Navigate(tag);
    }

    private void ConnectDmsUiBridge()
    {
        var watcher = DmsWatcherService.Instance;
        watcher.PropertyChanged -= OnDmsWatcherPropertyChanged;
        watcher.PropertyChanged += OnDmsWatcherPropertyChanged;
        watcher.TransactionPickerAsync = PickDmsTransactionAsync;
        UpdateDmsWatcherIndicator();
    }

    private void DisconnectDmsUiBridge()
    {
        var watcher = DmsWatcherService.Instance;
        watcher.PropertyChanged -= OnDmsWatcherPropertyChanged;
        if (watcher.TransactionPickerAsync == PickDmsTransactionAsync)
            watcher.TransactionPickerAsync = null;
        _watcherAssignmentWindow?.Close();
        _watcherAssignmentWindow = null;
        if (DmsWatcherProgress is not null)
        {
            DmsWatcherProgress.IsActive = false;
            DmsWatcherProgress.Visibility = Visibility.Collapsed;
            DmsWatcherQueueText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDmsWatcherPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateDmsWatcherIndicator);

    private void UpdateDmsWatcherIndicator()
    {
        var watcher = DmsWatcherService.Instance;
        DmsWatcherProgress.IsActive = watcher.IsBusy;
        DmsWatcherProgress.Visibility = watcher.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DmsWatcherQueueText.Text = watcher.QueueCount > 0 ? watcher.QueueCount.ToString() : string.Empty;
        DmsWatcherQueueText.Visibility = watcher.QueueCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(DmsNavItem, watcher.IsBusy
            ? $"Verarbeite: {watcher.CurrentFileName} · {watcher.CurrentPhase}"
            : watcher.IsRunning ? "DMS-Arbeitsordner wird überwacht." : "DMS-Überwachung ist nicht aktiv.");
    }

    private Task<int?> PickDmsTransactionAsync(DmsTransactionSelectionRequest request)
    {
        var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_session is null) { completion.TrySetResult(null); return; }
                _watcherAssignmentWindow = new DmsTransactionWindow(request);
                var result = await _watcherAssignmentWindow.ShowAsync();
                _watcherAssignmentWindow = null;
                completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                _watcherAssignmentWindow = null;
                completion.TrySetException(exception);
            }
        }))
        {
            completion.TrySetResult(null);
        }
        return completion.Task;
    }

    private void OnTransactionNavigationRequested(int transactionId)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_session is null || transactionId <= 0) return;
            _suppressNavigationSelection = true;
            RootNavigationView.SelectedItem = TransactionsNavItem;
            _suppressNavigationSelection = false;

            if (ContentFrame.Content is TransactionsPage current)
            {
                await current.FocusTransactionAsync(transactionId);
            }
            else
            {
                ContentFrame.Navigate(typeof(TransactionsPage), transactionId);
            }
        });
    }

    private void Navigate(string tag)
    {
        if (_session is null) return;
        if (tag == "Dashboard" && ContentFrame.CurrentSourcePageType != typeof(DashboardPage))
            ContentFrame.Navigate(typeof(DashboardPage));
        else if (tag == "Transactions" && ContentFrame.CurrentSourcePageType != typeof(TransactionsPage))
            ContentFrame.Navigate(typeof(TransactionsPage));
        else if (tag == "Accounts" && ContentFrame.CurrentSourcePageType != typeof(AccountsPage))
            ContentFrame.Navigate(typeof(AccountsPage));
        else if (tag == "Institutions" && ContentFrame.CurrentSourcePageType != typeof(InstitutionsPage))
            ContentFrame.Navigate(typeof(InstitutionsPage));
        else if (tag == "Addresses" && ContentFrame.CurrentSourcePageType != typeof(AddressesPage))
            ContentFrame.Navigate(typeof(AddressesPage));
        else if (tag == "Budget" && ContentFrame.CurrentSourcePageType != typeof(BudgetsPage))
            ContentFrame.Navigate(typeof(BudgetsPage));
        else if (tag == "Stwe" && ContentFrame.CurrentSourcePageType != typeof(StweSetsPage))
            ContentFrame.Navigate(typeof(StweSetsPage));
        else if (tag == "Properties" && ContentFrame.CurrentSourcePageType != typeof(PropertiesPage))
            ContentFrame.Navigate(typeof(PropertiesPage));
        else if (tag == "Wealth" && AppModules.IsWealthEnabled && ContentFrame.CurrentSourcePageType != typeof(WealthPage))
            ContentFrame.Navigate(typeof(WealthPage));
        else if (tag == "Home" && AppModules.IsHomeEnabled && ContentFrame.CurrentSourcePageType != typeof(HouseholdPage))
            ContentFrame.Navigate(typeof(HouseholdPage));
        else if (tag == "Dms" && AppModules.IsDmsEnabled && ContentFrame.CurrentSourcePageType != typeof(DmsPage))
            ContentFrame.Navigate(typeof(DmsPage));
        else if (tag == "Subscriptions" && AppModules.IsAbosEnabled && ContentFrame.CurrentSourcePageType != typeof(SubscriptionsPage))
            ContentFrame.Navigate(typeof(SubscriptionsPage));
        else if (tag == "Settings" && ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
            ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void ApplyModuleAccess()
    {
        try
        {
            new LicenseService().TryLoadAndApply(out _);
            AppModules.Load();
            StweNavItem.IsEnabled = PropertiesNavItem.IsEnabled = AppModules.IsPropertyEnabled;
            WealthNavItem.IsEnabled = AppModules.IsWealthEnabled;
            HomeNavItem.IsEnabled = AppModules.IsHomeEnabled;
            DmsNavItem.IsEnabled = AppModules.IsDmsEnabled;
            SubscriptionsNavItem.IsEnabled = AppModules.IsAbosEnabled;
            SettingsNavItem.IsEnabled = true;
        }
        catch
        {
            StweNavItem.IsEnabled = PropertiesNavItem.IsEnabled = WealthNavItem.IsEnabled = HomeNavItem.IsEnabled = DmsNavItem.IsEnabled = SubscriptionsNavItem.IsEnabled = false;
            SettingsNavItem.IsEnabled = true;
        }
    }
}
