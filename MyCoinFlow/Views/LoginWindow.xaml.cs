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
        private readonly MandantService _mandanten = new();

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

                // 1) Alle Mandanten-DBs ermitteln (mit dbo.Users)
                var list = await _mandanten.GetMandantenAsync();

                // 2) Aktive DB aus Persistenz holen
                var active = ConnectionStrings.ActiveDatabaseName;

                // 3) Prüfen, ob die aktive DB wirklich existiert (in master gegen DB_ID)
                var activeExists = await DbExistsAsync(active);

                // 4) Falls die aktive DB nicht existiert → auf sinnvolle Alternative umschalten
                if (!activeExists)
                {
                    string? fallback = null;

                    // Bevorzugt MyCoinFlowDB, falls vorhanden
                    if (await DbExistsAsync("MyCoinFlowDB"))
                        fallback = "MyCoinFlowDB";
                    else if (list.Count > 0)
                        fallback = list[0]; // erste gefundene DB (mit dbo.Users)

                    if (fallback != null)
                    {
                        ConnectionStrings.SetActiveDatabase(fallback);
                        active = fallback;
                        StatusText.Text = $"Aktive DB existierte nicht. Umgeschaltet auf: {fallback}.";
                    }
                    else
                    {
                        // Gar keine DB gefunden → UI im leeren Zustand belassen
                        DbCombo.ItemsSource = new List<string>();
                        StatusText.Text = "Keine Datenbank gefunden. Bitte in Admin einen Mandanten anlegen.";
                        FirstUserExpander.IsEnabled = false;
                        FirstUserExpander.IsExpanded = false;
                        return;
                    }
                }

                // 5) Dropdown aufbauen – aktive DB aufnehmen, auch wenn (noch) kein dbo.Users existiert
                if (!list.Contains(active))
                {
                    // aktive DB existiert (siehe oben), könnte aber (noch) kein dbo.Users haben → trotzdem anzeigen
                    list.Insert(0, active);
                }

                DbCombo.ItemsSource = list;
                DbCombo.SelectedItem = active;

                // Wechsel-Handler: sofort umschalten + Schema absichern
                DbCombo.SelectionChanged -= DbCombo_SelectionChanged;
                DbCombo.SelectionChanged += DbCombo_SelectionChanged;

                // 6) Jetzt Users-Schema in der aktiven DB sichern (legt dbo.Users an, falls noch nicht da)
                await _auth.EnsureSchemaAsync();

                // 7) Erstbenutzer-Hinweis
                var hasUsers = await _auth.HasAnyUserAsync();
                FirstUserExpander.IsEnabled = !hasUsers;
                FirstUserExpander.IsExpanded = !hasUsers;

                StatusText.Text = hasUsers
                    ? $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}. Bitte anmelden."
                    : $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}. Ersten Benutzer anlegen.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Fehler beim Start: " + ex.Message;
            }
        }

        private async void DbCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (DbCombo.SelectedItem is not string db) return;

                // Nur reagieren, wenn sich wirklich etwas ändert
                if (!string.Equals(db, ConnectionStrings.ActiveDatabaseName, StringComparison.OrdinalIgnoreCase))
                {
                    // Existenz prüfen, bevor wir umschalten
                    if (!await DbExistsAsync(db))
                    {
                        StatusText.Text = $"Die ausgewählte DB '{db}' existiert nicht.";
                        return;
                    }

                    _mandanten.SetActive(db); // persistiert + leert Pools
                    StatusText.Text = $"Aktive DB gewechselt zu: {db}.";

                    // In der neuen DB Schema absichern
                    await _auth.EnsureSchemaAsync();

                    // Erstbenutzerbereich gem. neuer DB
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
                var ok = await _auth.ValidateUserAsync(LoginUserBox.Text, LoginPwdBox.Password);
                if (!ok)
                {
                    StatusText.Text = "Ungültige Anmeldedaten.";
                    return;
                }

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

        // ---- master: DB_ID prüfen ----
        private static async Task<bool> DbExistsAsync(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName)) return false;

            const string masterCs = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";
            await using var c = new SqlConnection(masterCs);
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", dbName.Trim());
            var id = await cmd.ExecuteScalarAsync();
            return id != null && id != DBNull.Value;
        }
    }
}
