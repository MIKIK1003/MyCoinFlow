using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _auth = new();
        private readonly SqlExpressProvisioningService _provisioning = new();
        private readonly LicenseService _license = new();
        private readonly ActivationService _activation = new();
        private readonly DatabaseService _db = new();


        public LoginWindow()
        {
            InitializeComponent();
            Loaded += async (_, __) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                StatusText.Text = "";

                // --- Aktivierungs-Gate: Lizenz oder Testversion erforderlich ---
                var activationStatus = _activation.GetStatus(out var activationMessage);

                if (activationStatus != AppActivationStatus.Licensed &&
                    activationStatus != AppActivationStatus.Trial)
                {
                    StatusText.Text = activationMessage;
                    ShowLicenseUiAndLock(activationMessage);
                    return;
                }

                // Aktivierung ok -> Login normal
                HideLicenseUiAndUnlock();

                // PHASE 4: Auf frischen PCs sicherstellen, dass es mindestens eine Default-DB gibt.
                StatusText.Text = "Initialisiere Datenbank…";
                await _provisioning.EnsureDefaultDatabaseExistsAsync();

                // DB-Liste direkt aus master von .\SQLEXPRESS holen
                var list = await GetUserDatabasesAsync();

                // Aktive DB aus Persistenz holen
                var active = ConnectionStrings.ActiveDatabaseName;

                // Aktive DB existiert?
                var activeExists = await DbExistsAsync(active);

                // Fallback, wenn aktive DB nicht existiert
                if (!activeExists)
                {
                    string? fallback = null;

                    if (await DbExistsAsync(ConnectionStrings.DefaultDatabaseName))
                        fallback = ConnectionStrings.DefaultDatabaseName;
                    else if (list.Count > 0)
                        fallback = list[0];

                    if (fallback != null)
                    {
                        ConnectionStrings.SetActiveDatabase(fallback);
                        active = fallback;
                        StatusText.Text = $"Aktive DB existierte nicht. Umgeschaltet auf: {fallback}.";
                    }
                    else
                    {
                        DbCombo.ItemsSource = new List<string>();
                        StatusText.Text = "Keine Datenbank gefunden.";
                        FirstUserExpander.IsEnabled = false;
                        FirstUserExpander.IsExpanded = false;
                        return;
                    }
                }

                // Dropdown
                if (!list.Contains(active))
                    list.Insert(0, active);

                DbCombo.ItemsSource = list;
                DbCombo.SelectedItem = active;

                DbCombo.SelectionChanged -= DbCombo_SelectionChanged;
                DbCombo.SelectionChanged += DbCombo_SelectionChanged;

                // ========= WICHTIG: Schema-Update Transaktion (BudgetDatum) =========
                StatusText.Text = "Aktualisiere Schema (BudgetDatum)…";
                _db.EnsureTransaktionBudgetDatumColumn();

                // Users-Schema sicherstellen (legt dbo.Users an, falls noch nicht da)
                StatusText.Text = "Prüfe Benutzer-Schema…";
                await _auth.EnsureSchemaAsync();

                // Erstbenutzer
                var hasUsers = await _auth.HasAnyUserAsync();
                FirstUserExpander.IsEnabled = !hasUsers;
                FirstUserExpander.IsExpanded = !hasUsers;

                StatusText.Text = hasUsers
                    ? $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}. Bitte anmelden."
                    : $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}. Ersten Benutzer anlegen.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "Fehler beim Start: " + ex.Message + Environment.NewLine +
                    "Hinweis: Prüfe SQL Server Express (SQLEXPRESS) und ob das Template-Backup in ProgramData vorhanden ist.";
            }
        }

        private async void DbCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (DbCombo.SelectedItem is not string db) return;

                if (!string.Equals(db, ConnectionStrings.ActiveDatabaseName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await DbExistsAsync(db))
                    {
                        StatusText.Text = $"Die ausgewählte DB '{db}' existiert nicht.";
                        return;
                    }

                    ConnectionStrings.SetActiveDatabase(db);
                    StatusText.Text = $"Aktive DB gewechselt zu: {db}.";

                    // ========= WICHTIG: Schema-Update Transaktion (BudgetDatum) =========
                    StatusText.Text = "Aktualisiere Schema (BudgetDatum)…";
                    _db.EnsureTransaktionBudgetDatumColumn();

                    await _auth.EnsureSchemaAsync();

                    var hasUsers = await _auth.HasAnyUserAsync();
                    FirstUserExpander.IsEnabled = !hasUsers;
                    FirstUserExpander.IsExpanded = !hasUsers;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Fehler beim Wechsel der DB: " + ex.Message;
            }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "";

                var username = (LoginUserBox.Text ?? "").Trim();

                var ok = await _auth.ValidateUserAsync(username, LoginPwdBox.Password);
                if (!ok)
                {
                    StatusText.Text = "Ungültige Anmeldedaten.";
                    return;
                }

                // ✅ Admin-Rolle aus DB holen und in Session ablegen
                var isAdmin = await _auth.GetIsAdminAsync(username);
                CurrentUserContext.SignIn(username, isAdmin);

                var main = new MyCoinFlow.MainWindow();
                main.Show();
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Login-Fehler: " + ex.Message;
            }
        }


        private async void CreateFirstUser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "";

                var username = NewUserBox.Text?.Trim() ?? "";
                var pwd1 = NewPwd1Box.Password ?? "";
                var pwd2 = NewPwd2Box.Password ?? "";

                if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 32)
                {
                    StatusText.Text = "Benutzername: 3–32 Zeichen, nur A-Z, a-z, 0-9, ., _, -.";
                    return;
                }
                if (pwd1.Length < 6)
                {
                    StatusText.Text = "Das Passwort muss mindestens 6 Zeichen haben.";
                    return;
                }
                if (pwd1 != pwd2)
                {
                    StatusText.Text = "Die Passwörter stimmen nicht überein.";
                    return;
                }

                await _auth.CreateFirstUserAsync(username, pwd1);

                StatusText.Text = "Erster Benutzer angelegt. Sie können sich oben anmelden.";
                FirstUserExpander.IsEnabled = false;
                FirstUserExpander.IsExpanded = false;

                LoginUserBox.Text = username;
                LoginPwdBox.Password = "";
                NewUserBox.Text = "";
                NewPwd1Box.Password = "";
                NewPwd2Box.Password = "";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Fehler beim Anlegen: " + ex.Message;
            }
        }

        // --- master helpers (guaranteed to point to .\SQLEXPRESS via ConnectionStrings.Master) ---

        private static async Task<bool> DbExistsAsync(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName)) return false;

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName.Trim());

            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }

        private static async Task<List<string>> GetUserDatabasesAsync()
        {
            var list = new List<string>();

            await using var c = new SqlConnection(ConnectionStrings.Master);
            await c.OpenAsync();

            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT name
FROM sys.databases
WHERE database_id > 4
ORDER BY name;
";

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var name = r.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }

            return list;
        }

        private void StackPanel_TimeChanged(object sender, MaterialDesignThemes.Wpf.TimeChangedEventArgs e) { }
        private void StackPanel_TimeChanged_1(object sender, MaterialDesignThemes.Wpf.TimeChangedEventArgs e) { }

        private void ShowLicenseUiAndLock(string message)
        {
            LicensePanel.Visibility = Visibility.Visible;
            LicenseStatusText.Text = message ?? "";

            DbCombo.IsEnabled = false;
            LoginUserBox.IsEnabled = false;
            LoginPwdBox.IsEnabled = false;
            LoginButton.IsEnabled = false;

            FirstUserExpander.IsEnabled = false;
            FirstUserExpander.IsExpanded = false;

            if (LicenseKeyBox != null)
                LicenseKeyBox.IsEnabled = true;

            if (SaveLicenseButton != null)
                SaveLicenseButton.IsEnabled = true;

            if (ImportLicenseFileButton != null)
                ImportLicenseFileButton.IsEnabled = true;

            if (StartTrialButton != null)
                StartTrialButton.IsEnabled = true;
        }

        private void HideLicenseUiAndUnlock()
        {
            LicensePanel.Visibility = Visibility.Collapsed;
            LicenseStatusText.Text = "";

            // Login wieder aktiv
            DbCombo.IsEnabled = true;
            LoginUserBox.IsEnabled = true;
            LoginPwdBox.IsEnabled = true;
            LoginButton.IsEnabled = true;

            // Erstbenutzer wird später in InitializeAsync() anhand HasAnyUser gesetzt
        }

        private async void ImportLicenseFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LicenseStatusText.Text = "";

                if (_license.ImportLicenseFile(this, out var message))
                {
                    LicenseStatusText.Text = message;
                    await InitializeAsync();
                    return;
                }

                LicenseStatusText.Text = message;
            }
            catch (Exception ex)
            {
                LicenseStatusText.Text = "Fehler beim Import: " + ex.Message;
            }
        }

        private async void StartTrialButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LicenseStatusText.Text = "";

                if (_activation.StartTrial(out var message))
                {
                    LicenseStatusText.Text = message;
                    await InitializeAsync();
                    return;
                }

                LicenseStatusText.Text = message;
            }
            catch (Exception ex)
            {
                LicenseStatusText.Text = "Fehler beim Start der Testversion: " + ex.Message;
            }
        }

        private async void SaveLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LicenseStatusText.Text = "";

                var key = (LicenseKeyBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    LicenseStatusText.Text = "Bitte Lizenzschlüssel eingeben.";
                    return;
                }

                if (!_license.TryValidate(key, out var payload, out var err))
                {
                    LicenseStatusText.Text = "Ungültige Lizenz: " + err;
                    return;
                }

                _license.SaveKey(key);

                // Nach Speichern nochmals prüfen/anwenden
                if (!_license.TryLoadAndApply(out var msg))
                {
                    LicenseStatusText.Text = "Lizenz gespeichert, aber ungültig: " + msg;
                    ShowLicenseUiAndLock(msg);
                    return;
                }

                LicenseStatusText.Text = $"Lizenz OK ({payload.Edition}).";
                HideLicenseUiAndUnlock();

                // Jetzt den normalen Startablauf nochmals ausführen (DB/Users etc.)
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                LicenseStatusText.Text = "Fehler: " + ex.Message;
            }
        }



    }
}
