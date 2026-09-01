using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using MyCoinFlow.WinUI.Security;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class LoginPage : UserControl
{
    private readonly LoginRepository _repository = new();
    private readonly ActivationService _activationService = new();
    private bool _initializing;
    private bool _busy;
    private bool _activationAllowed;

    public LoginPage()
    {
        InitializeComponent();
    }

    public event EventHandler<LoginSession>? LoginSucceeded;

    public async Task InitializeAsync()
    {
        if (_busy) return;
        SetBusy(true);
        _initializing = true;
        try
        {
            var activation = _activationService.Check();
            if (!activation.IsAllowed)
            {
                _activationAllowed = false;
                ActivationPanel.Visibility = Visibility.Visible;
                ActivationMessageText.Text = activation.Message;
                StartTrialButton.Visibility = activation.CanStartTrial ? Visibility.Visible : Visibility.Collapsed;
                DatabaseBox.IsEnabled = false;
                ShowStatus(activation.Message, InfoBarSeverity.Warning);
                return;
            }
            _activationAllowed = true;
            ActivationPanel.Visibility = Visibility.Collapsed;
            ShowStatus("Mandanten werden geladen …", InfoBarSeverity.Informational);
            var databases = await _repository.GetDatabasesAsync();
            DatabaseBox.ItemsSource = databases;
            if (databases.Count == 0)
            {
                DatabaseStatusText.Text = "Keine MyCoinFlow-Datenbank gefunden.";
                ShowStatus("Auf .\\SQLEXPRESS wurde keine Benutzerdatenbank gefunden.", InfoBarSeverity.Warning);
                return;
            }

            var active = databases.FirstOrDefault(database =>
                string.Equals(database, ConnectionStrings.ActiveDatabaseName, StringComparison.OrdinalIgnoreCase))
                ?? databases[0];
            DatabaseBox.SelectedItem = active;
            await ActivateSelectedDatabaseAsync(active);
            StatusInfoBar.IsOpen = false;
            UsernameBox.Focus(FocusState.Programmatic);
        }
        catch (Exception exception)
        {
            ShowStatus("Anmeldung konnte nicht initialisiert werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _initializing = false;
            SetBusy(false);
        }
    }

    public async Task ResetAsync()
    {
        PasswordBox.Password = string.Empty;
        StatusInfoBar.IsOpen = false;
        await InitializeAsync();
    }

    private async void OnDatabaseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || DatabaseBox.SelectedItem is not string database) return;
        SetBusy(true);
        try
        {
            await ActivateSelectedDatabaseAsync(database);
            StatusInfoBar.IsOpen = false;
        }
        catch (Exception exception)
        {
            ShowStatus("Mandant konnte nicht gewechselt werden: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ActivateSelectedDatabaseAsync(string database)
    {
        await _repository.SelectDatabaseAsync(database);
        var hasUsers = await _repository.HasUsersAsync();
        FirstUserExpander.IsEnabled = !hasUsers;
        FirstUserExpander.IsExpanded = !hasUsers;
        LoginButton.IsEnabled = hasUsers;
        UsernameBox.IsEnabled = hasUsers;
        PasswordBox.IsEnabled = hasUsers;
        DatabaseStatusText.Text = hasUsers
            ? $"Aktive DB: {database}. Bitte anmelden."
            : $"Aktive DB: {database}. Ersten Benutzer anlegen.";
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e) => await LoginAsync();

    private async void OnLoginFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await LoginAsync();
    }

    private async Task LoginAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var session = await _repository.AuthenticateAsync(UsernameBox.Text, PasswordBox.Password);
            if (session is null)
            {
                ShowStatus("Benutzername oder Passwort ist ungültig.", InfoBarSeverity.Error);
                PasswordBox.SelectAll();
                PasswordBox.Focus(FocusState.Programmatic);
                return;
            }

            PasswordBox.Password = string.Empty;
            StatusInfoBar.IsOpen = false;
            LoginSucceeded?.Invoke(this, session);
        }
        catch (Exception exception)
        {
            ShowStatus("Anmeldung fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCreateFirstUserClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (NewPasswordBox.Password != RepeatPasswordBox.Password)
        {
            ShowStatus("Die Passwörter stimmen nicht überein.", InfoBarSeverity.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            await _repository.CreateFirstUserAsync(NewUsernameBox.Text, NewPasswordBox.Password);
            UsernameBox.Text = NewUsernameBox.Text.Trim();
            NewUsernameBox.Text = string.Empty;
            NewPasswordBox.Password = string.Empty;
            RepeatPasswordBox.Password = string.Empty;
            FirstUserExpander.IsExpanded = false;
            FirstUserExpander.IsEnabled = false;
            UsernameBox.IsEnabled = true;
            PasswordBox.IsEnabled = true;
            LoginButton.IsEnabled = true;
            ShowStatus("Administrator wurde angelegt. Sie können sich jetzt anmelden.", InfoBarSeverity.Success);
            PasswordBox.Focus(FocusState.Programmatic);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnStartTrialClick(object sender, RoutedEventArgs e)
    {
        var activation = _activationService.StartTrial();
        if (!activation.IsAllowed)
        {
            ActivationMessageText.Text = activation.Message;
            return;
        }
        ActivationPanel.Visibility = Visibility.Collapsed;
        StatusInfoBar.IsOpen = false;
        await InitializeAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        DatabaseBox.IsEnabled = !busy && _activationAllowed;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
