using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public const string FinanceSectionKey = "Finance";

    private readonly Dictionary<string, UIElement> _sections = new();
    private string? _requestedSection;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HeadingText.Text = CurrentUserContext.IsAdmin ? "Einstellungen (Admin)" : "Einstellungen (User)";
        FinanceItem.Visibility = TenantsItem.Visibility = UpdateItem.Visibility =
            CurrentUserContext.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!CurrentUserContext.IsAdmin && SettingsHost.Content is FinanceSettingsControl)
        {
            SettingsHost.Content = null;
            SettingsNavigation.SelectedItem = null;
            _sections.Remove(FinanceSectionKey);
        }
        SelectRequestedSection();
        SettingsNavigation.SelectedItem ??= SettingsNavigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(value => value.Visibility == Visibility.Visible);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _requestedSection = e.Parameter as string;
        if (XamlRoot is not null)
            SelectRequestedSection();
    }

    private void SelectRequestedSection()
    {
        if (string.IsNullOrWhiteSpace(_requestedSection)) return;
        var item = SettingsNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(value =>
                value.Tag as string == _requestedSection &&
                value.Visibility == Visibility.Visible);
        if (item is not null)
            SettingsNavigation.SelectedItem = item;
        _requestedSection = null;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key) return;
        if (!_sections.TryGetValue(key, out var section))
        {
            section = key switch
            {
                FinanceSectionKey when CurrentUserContext.IsAdmin => new FinanceSettingsControl(),
                "NumberRanges" => new NumberRangesSettingsControl(),
                "AccountStructure" => new AccountStructureSettingsControl(),
                "Transactions" => new TransactionSettingsControl(),
                "CreditCards" => new CreditCardSettingsControl(),
                "SubscriptionCategories" => new SubscriptionCategoriesSettingsControl(),
                "Tenants" => new TenantSettingsControl(),
                "Update" => new UpdateSettingsControl(),
                "Backup" => new BackupSettingsControl(),
                "Paths" => new PathsSettingsControl(),
                "License" => new LicenseSettingsControl(),
                _ => new TextBlock { Text = "Einstellung nicht verfügbar." }
            };
            _sections[key] = section;
        }
        SettingsHost.Content = section;
    }
}
