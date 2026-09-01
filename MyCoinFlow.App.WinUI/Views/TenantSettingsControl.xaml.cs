using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class TenantSettingsControl : UserControl
{
    private readonly MandantService _tenants = new();
    private readonly DbCopyService _copy = new();
    private readonly AuthService _auth = new();

    public TenantSettingsControl() { InitializeComponent(); Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e) { await LoadDatabasesAsync(); await LoadUsersAsync(); }
    private async Task LoadDatabasesAsync() { try { var values = await _tenants.GetAllDatabaseNamesAsync(); SourceDatabaseBox.ItemsSource = values; TargetDatabaseBox.ItemsSource = values; } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); } }
    private async void OnCreateTenantClick(object sender, RoutedEventArgs e) { var name = NewTenantNameBox.Text.Trim(); if (name.Length < 3) { Show("Bitte einen gültigen DB-Namen eingeben (mindestens 3 Zeichen).", InfoBarSeverity.Warning); return; } try { await _tenants.CreateEmptyFromTemplateAsync(name); await LoadDatabasesAsync(); Show($"Mandant „{name}“ erstellt.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Fehler: " + ex.Message, InfoBarSeverity.Error); } }
    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var source = SourceDatabaseBox.Text.Trim(); var target = TargetDatabaseBox.Text.Trim(); if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || source.Equals(target, StringComparison.OrdinalIgnoreCase)) { Show("Bitte unterschiedliche Quell- und Ziel-Datenbanken wählen.", InfoBarSeverity.Warning); return; }
        try { var options = new DbCopyOptions { CopyNumberRanges = CopyNumberRangesBox.IsChecked == true, CopyKontenstruktur = CopyAccountsBox.IsChecked == true, CopyAdressen = CopyAddressesBox.IsChecked == true, CopyAliase = CopyAliasesBox.IsChecked == true, CopyGeldinstitute = CopyInstitutionsBox.IsChecked == true, CopyImportSchemas = CopyImportBox.IsChecked == true, CopyKategorieKonto = CopyCategoryBox.IsChecked == true, CreateBudgetzeitraum = CopyBudgetBox.IsChecked == true, BudgetYear = DateTime.Today.Year }; Show($"Kopiere von „{source}“ nach „{target}“…", InfoBarSeverity.Informational); await _copy.CopyAsync(source, target, options, createTargetIfMissing: false); Show("Kopieren erfolgreich abgeschlossen.", InfoBarSeverity.Success); }
        catch (Exception ex) { Show("Fehler beim Kopieren: " + ex.Message, InfoBarSeverity.Error); }
    }
    private async Task LoadUsersAsync() { try { ActiveDatabaseText.Text = $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}"; await _auth.EnsureSchemaAsync(); var users = await _auth.GetUsersAsync(); UsersList.ItemsSource = users; Show(users.Count == 0 ? "Noch keine Benutzer vorhanden." : $"{users.Count} Benutzer geladen.", InfoBarSeverity.Informational); } catch (Exception ex) { Show("Fehler beim Laden der Benutzer: " + ex.Message, InfoBarSeverity.Error); } }
    private async void OnRefreshUsersClick(object sender, RoutedEventArgs e) => await LoadUsersAsync();
    private async void OnAddUserClick(object sender, RoutedEventArgs e) { var dialog = new UserEditorDialog { XamlRoot = XamlRoot }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; try { await _auth.CreateUserAsync(dialog.Username, dialog.Password, dialog.Email, dialog.IsAdmin); await LoadUsersAsync(); Show($"Benutzer „{dialog.Username}“ wurde angelegt.", InfoBarSeverity.Success); } catch (Exception ex) { Show("Fehler: " + ex.Message, InfoBarSeverity.Error); } }
    private async void OnResetPasswordClick(object sender, RoutedEventArgs e) { if (UsersList.SelectedItem is not AuthService.UserRow user) { Show("Bitte zuerst einen Benutzer auswählen.", InfoBarSeverity.Warning); return; } var dialog = new PasswordResetDialog(user.Username) { XamlRoot = XamlRoot }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; try { await _auth.ResetPasswordAsync(user.Id, dialog.Password); Show($"Passwort für „{user.Username}“ wurde zurückgesetzt.", InfoBarSeverity.Success); } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); } }
    private async void OnToggleActiveClick(object sender, RoutedEventArgs e) { if (UsersList.SelectedItem is not AuthService.UserRow user) { Show("Bitte zuerst einen Benutzer auswählen.", InfoBarSeverity.Warning); return; } try { await _auth.SetUserActiveAsync(user.Id, !user.IsActive); await LoadUsersAsync(); } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); } }
    private async void OnToggleAdminClick(object sender, RoutedEventArgs e) { if (UsersList.SelectedItem is not AuthService.UserRow user) { Show("Bitte zuerst einen Benutzer auswählen.", InfoBarSeverity.Warning); return; } try { await _auth.SetUserAdminAsync(user.Id, !user.IsAdmin); await LoadUsersAsync(); } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); } }
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}

internal sealed class UserEditorDialog : ContentDialog
{
    private readonly TextBox _username = new() { Header = "Username" }; private readonly TextBox _email = new() { Header = "E-Mail" }; private readonly PasswordBox _password = new() { Header = "Passwort" }; private readonly PasswordBox _repeat = new() { Header = "Passwort wiederholen" }; private readonly CheckBox _admin = new() { Content = "Ist Admin" }; private readonly TextBlock _error = new();
    public UserEditorDialog() { Title = "Neuer Benutzer"; PrimaryButtonText = "Anlegen"; CloseButtonText = "Abbrechen"; Content = new StackPanel { Width = 420, Spacing = 8, Children = { _username, _email, _password, _repeat, _admin, _error } }; PrimaryButtonClick += (_, args) => { if (_password.Password != _repeat.Password || _username.Text.Trim().Length < 3 || _password.Password.Length < 6) { args.Cancel = true; _error.Text = "Username: 3–64 Zeichen; Passwort: mindestens 6 Zeichen und beide Eingaben identisch."; } }; }
    public string Username => _username.Text.Trim(); public string? Email => string.IsNullOrWhiteSpace(_email.Text) ? null : _email.Text.Trim(); public string Password => _password.Password; public bool IsAdmin => _admin.IsChecked == true;
}

internal sealed class PasswordResetDialog : ContentDialog
{
    private readonly PasswordBox _password = new() { Header = "Neues Passwort" }; private readonly PasswordBox _repeat = new() { Header = "Passwort wiederholen" }; private readonly TextBlock _error = new();
    public PasswordResetDialog(string username) { Title = $"Passwort zurücksetzen – {username}"; PrimaryButtonText = "Speichern"; CloseButtonText = "Abbrechen"; Content = new StackPanel { Width = 420, Spacing = 8, Children = { _password, _repeat, _error } }; PrimaryButtonClick += (_, args) => { if (_password.Password.Length < 6 || _password.Password != _repeat.Password) { args.Cancel = true; _error.Text = "Mindestens 6 Zeichen; beide Eingaben müssen identisch sein."; } }; }
    public string Password => _password.Password;
}
