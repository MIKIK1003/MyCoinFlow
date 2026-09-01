using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private readonly Dictionary<string, UIElement> _sections = new();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HeadingText.Text = CurrentUserContext.IsAdmin ? "Einstellungen (Admin)" : "Einstellungen (User)";
        TenantsItem.Visibility = UpdateItem.Visibility = CurrentUserContext.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        SettingsNavigation.SelectedItem ??= SettingsNavigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(value => value.Visibility == Visibility.Visible);
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key) return;
        if (!_sections.TryGetValue(key, out var section))
        {
            section = key switch
            {
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
