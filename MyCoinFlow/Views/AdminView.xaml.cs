using Microsoft.Win32;
using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using MyCoinFlow.Models;

namespace MyCoinFlow.Views
{
    public partial class AdminView : UserControl
    {
        private readonly DbCopyService _copy = new();
        private readonly DbBackupService _backup = new();
        private readonly DbRestoreService _restore = new();
        private readonly DatabaseService _dbSvc = new();

        private readonly MandantService _mandants = new();
        private readonly AuthService _auth = new();

        public AdminView()
        {
            InitializeComponent();
        }

        // ===== Helpers =====
        private T? El<T>(string name) where T : FrameworkElement => FindName(name) as T;
        private void SetText(string name, string txt) { if (El<TextBlock>(name) is TextBlock tb) tb.Text = txt ?? ""; }

        private static T? FindByNameCaseInsensitive<T>(FrameworkElement root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            if (root is T hit && string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase)) return hit;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(root, i) is FrameworkElement fe)
                {
                    var found = FindByNameCaseInsensitive<T>(fe, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private FrameworkElement? TryCreateView(string fullTypeName)
        {
            try
            {
                var t = Type.GetType(fullTypeName, throwOnError: false);
                if (t == null || !typeof(FrameworkElement).IsAssignableFrom(t)) return null;
                return Activator.CreateInstance(t) as FrameworkElement;
            }
            catch { return null; }
        }

        private void SetHostIfEmpty(string hostName, Func<FrameworkElement?> createView)
        {
            var host = El<ContentControl>(hostName) ?? FindByNameCaseInsensitive<ContentControl>(this, hostName);
            if (host != null && host.Content == null)
            {
                var view = createView();
                if (view != null) host.Content = view;
            }
        }

        // ===== Loaded / Navigation =====
        private async void AdminView_Loaded(object sender, RoutedEventArgs e)
        {
            try { new DatabaseService().EnsureNumberRangeRulesTable(); } catch { }

            var nav = El<ListBox>("NavList");
            if (nav != null)
            {
                nav.SelectionChanged -= NavList_SelectionChanged;
                nav.SelectionChanged += NavList_SelectionChanged;

                var start = nav.Items.OfType<ListBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, "Kontenplan", StringComparison.OrdinalIgnoreCase));
                nav.SelectedItem = start ?? nav.Items.OfType<ListBoxItem>().FirstOrDefault();
            }
            ShowSection("Kontenplan");
            RoleHeaderText.Text = CurrentUserContext.IsAdmin ? "Einstellungen (Admin)" : "Einstellungen (User)";
            ApplyRoleVisibility();
            EnsureKontenHosts();
            EnsureCreditCardMappingInline();
            AttachNumberRangesView();
            EnsureUpdatesHost();
            EnsurePathsHost();
            MandantenTab_SelectionChanged(MandantenTab, null);
            BackupTab_SelectionChanged(BackupTab, null);
            PathsTab_SelectionChanged(PathsTab, null);

            await LoadCreditCardSchemasAsync();
            await LoadCopyCombosAsync();
            SetBackupDefaultPath();

            await LoadUsersAsync();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = ((El<ListBox>("NavList")?.SelectedItem as ListBoxItem)?.Tag as string) ?? "Kontenplan";
            ShowSection(tag);

            if (string.Equals(tag, "Lizenz", StringComparison.OrdinalIgnoreCase))
            {
                var lic = new LicenseService();
                lic.TryLoadAndApply(out var msg);
                LicenseInfoText.Text = msg;
            }


            // Nicht-Admin darf Mandanten/Update nicht öffnen
            if (!CurrentUserContext.IsAdmin &&
                (string.Equals(tag, "Mandanten", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tag, "Update", StringComparison.OrdinalIgnoreCase)))
            {
                // auf Kontenplan zurück
                ShowSection("Kontenplan");
                return;
            }


            if (string.Equals(tag, "Backup", StringComparison.OrdinalIgnoreCase))
                SetBackupDefaultPath();

            if (string.Equals(tag, "Mandanten", StringComparison.OrdinalIgnoreCase))
                _ = LoadUsersAsync();
        }

        private void ShowSection(string key)
        {
            void SetVis(string gridName, bool on)
            {
                var g = El<Grid>(gridName);
                if (g != null) g.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            bool isNum = string.Equals(key, "Nummernkreise", StringComparison.OrdinalIgnoreCase);
            bool isKonten = string.Equals(key, "Kontenplan", StringComparison.OrdinalIgnoreCase);
            bool isKredit = string.Equals(key, "Kreditkarten", StringComparison.OrdinalIgnoreCase);
            bool isMand = string.Equals(key, "Mandanten", StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(key, "Update", StringComparison.OrdinalIgnoreCase);
            bool isBackup = string.Equals(key, "Backup", StringComparison.OrdinalIgnoreCase);
            bool isPfade = string.Equals(key, "Pfade", StringComparison.OrdinalIgnoreCase);

            bool isLiz = string.Equals(key, "Lizenz", StringComparison.OrdinalIgnoreCase);


            SetVis("SecNummernkreise", isNum);
            SetVis("SecKontenplan", isKonten);
            SetVis("SecKreditkarten", isKredit);
            SetVis("SecMandanten", isMand);
            SetVis("SecUpdate", isUpdate);
            SetVis("SecBackup", isBackup);
            SetVis("SecPfade", isPfade);
            SetVis("SecLizenz", isLiz);


            if (isKredit) EnsureCreditCardMappingInline();
            if (isPfade) EnsurePathsHost();
        }

        // ===== Nummernkreise-Host =====
        private void AttachNumberRangesView()
        {
            try { SetHostIfEmpty("NumberRangesHost", () => TryCreateView("MyCoinFlow.Views.AdminNumberRangesView")); }
            catch { }
        }

        // ===== Kontenplan: Hosts / Import / Export =====
        private void EnsureKontenHosts()
        {
            try
            {
                // ===== Konten-Art =====
                var fullArt = TryCreateView("MyCoinFlow.Views.KontenArtView");
                if (fullArt != null)
                {
                    var vm = fullArt.DataContext;

                    var listView = TryCreateView("MyCoinFlow.Views.KontenArtListView");
                    if (listView != null) listView.DataContext = vm;
                    var listHost = El<ContentControl>("KontenArtListHost");
                    if (listHost != null) listHost.Content = listView;

                    var actionsView = TryCreateView("MyCoinFlow.Views.KontenArtActionsView");
                    if (actionsView != null) actionsView.DataContext = vm;
                    var actionsHost = El<ContentControl>("KontenplanActionsHost");
                    if (actionsHost != null) actionsHost.Content = actionsView;
                }

                // ===== Konten-Gruppe =====
                var fullGrp = TryCreateView("MyCoinFlow.Views.KontenGruppeView");
                if (fullGrp != null)
                {
                    var vm = fullGrp.DataContext;

                    var listView = TryCreateView("MyCoinFlow.Views.KontenGruppeListView");
                    if (listView != null) listView.DataContext = vm;
                    var listHost = El<ContentControl>("KontenGruppeListHost");
                    if (listHost != null) listHost.Content = listView;
                }

                // ===== Konten-Untergruppe =====
                var fullUg = TryCreateView("MyCoinFlow.Views.KontenUnterGruppeView");
                if (fullUg != null)
                {
                    var vm = fullUg.DataContext;

                    var listView = TryCreateView("MyCoinFlow.Views.KontenUnterGruppeListView");
                    if (listView != null) listView.DataContext = vm;
                    var listHost = El<ContentControl>("KontenUnterGruppeListHost");
                    if (listHost != null) listHost.Content = listView;
                }
            }
            catch { }
        }





        private void ImportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var t = Type.GetType("MyCoinFlow.Views.KontenplanImportDialog", throwOnError: false);
                if (t == null)
                {
                    MessageBox.Show("Import-Dialog nicht gefunden.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Window? ResolveOwner(Window? dialogToOpen)
                {
                    try
                    {
                        var active = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
                        var owner = active ?? Application.Current?.MainWindow;
                        if (owner != null && dialogToOpen != null && !ReferenceEquals(owner, dialogToOpen))
                            return owner;
                    }
                    catch { }
                    return null;
                }

                if (typeof(Window).IsAssignableFrom(t))
                {
                    var dlg = Activator.CreateInstance(t) as Window;
                    if (dlg == null)
                    {
                        MessageBox.Show("Import-Dialog konnte nicht erzeugt werden.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var owner = ResolveOwner(dlg);
                    if (owner != null) dlg.Owner = owner;
                    else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                    dlg.ShowDialog();
                }
                else
                {
                    if (Activator.CreateInstance(t) is FrameworkElement fe)
                    {
                        var host = new Window
                        {
                            Title = "Kontenplan importieren",
                            Content = fe,
                            SizeToContent = SizeToContent.WidthAndHeight,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        var owner = ResolveOwner(host);
                        if (owner != null) host.Owner = owner;
                        else host.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                        host.ShowDialog();
                    }
                    else
                    {
                        MessageBox.Show("Import-UI konnte nicht instanziert werden.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import fehlgeschlagen:\n" + ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Kontenplan exportieren",
                Filter = "Excel-Datei (*.xlsx)|*.xlsx",
                FileName = "Kontenplan.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var t = Type.GetType("MyCoinFlow.Importing.KontenplanExcelExporter", throwOnError: false);
                    if (t == null)
                    {
                        MessageBox.Show("Exporter nicht gefunden.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var exporter = Activator.CreateInstance(t);
                    t.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance)
                     ?.Invoke(exporter, new object[] { sfd.FileName });

                    MessageBox.Show("Kontenplan wurde exportiert.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export fehlgeschlagen:\n" + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadCreditCardSchemasAsync()
        {
            try
            {
                var all = _dbSvc.ImportSchemasGetAll();
                var list = all.Where(s => !s.IsMaster)
                              .OrderBy(s => s.Name)
                              .ToList();

                var cb = El<ComboBox>("CC_SchemaCombo");
                if (cb != null)
                {
                    cb.ItemsSource = list;
                    if (cb.Items.Count > 0 && cb.SelectedItem == null)
                        cb.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Schemas konnten nicht geladen werden:\n" + ex.Message,
                                "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureCreditCardMappingInline()
        {
            try
            {
                var host = El<ContentControl>("CreditCardImportMappingHost")
                           ?? FindByNameCaseInsensitive<ContentControl>(this, "CreditCardImportMappingHost");
                if (host == null) return;

                host.Content = null;
                host.Content = new CreditCardImportMappingView();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kreditkarten-Editor konnte nicht geladen werden:\n" + ex,
                                "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureUpdatesHost()
        {
            try { SetHostIfEmpty("UpdatesHost", () => new MyCoinFlow.Views.Admin.AdminUpdatesControl()); }
            catch { }
        }

        private void EnsurePathsHost()
        {
            try { SetHostIfEmpty("PathsHost", () => new MyCoinFlow.Views.AdminPathsView()); }
            catch { }
        }

        // ===== Mandanten: neue leere DB =====
        private async void CreateEmptyTenant_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("TenantCreateStatus", "");
                var name = El<TextBox>("NewTenantNameBox")?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                {
                    SetText("TenantCreateStatus", "Bitte gültigen DB-Namen eingeben (≥ 3 Zeichen).");
                    return;
                }

                await _mandants.CreateEmptyFromTemplateAsync(name);

                SetText("TenantCreateStatus", $"Mandant '{name}' erstellt.");
                await LoadCopyCombosAsync();
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                SetText("TenantCreateStatus", "Fehler: " + ex.Message);
            }
        }

        // ===== DB-Kopie =====
        private async Task<List<string>> ListAllUserDatabasesAsync()
        {
            var result = new List<string>();

            await using var conn = new SqlConnection(ConnectionStrings.Master);
            await conn.OpenAsync();

            const string sql = @"
SELECT name
FROM sys.databases
WHERE database_id > 4
ORDER BY name;";

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    result.Add(r.GetString(0));
            }

            return result.OrderBy(n => n.StartsWith("MyCoinFlow", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                         .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        private async Task LoadCopyCombosAsync()
        {
            try
            {
                var list = await ListAllUserDatabasesAsync();
                var src = El<ComboBox>("Copy_SourceDbCombo");
                var dst = El<ComboBox>("Copy_TargetDbCombo");
                if (src != null) src.ItemsSource = list.ToList();
                if (dst != null) dst.ItemsSource = list.ToList();
            }
            catch (Exception ex)
            {
                SetText("Copy_StatusText", "Fehler beim Laden der DB-Liste: " + ex.Message);
            }
        }

        private async void Copy_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("Copy_StatusText", "");

                var sourceDb = (El<ComboBox>("Copy_SourceDbCombo")?.Text ?? "").Trim();
                var targetDb = (El<ComboBox>("Copy_TargetDbCombo")?.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(sourceDb)) { SetText("Copy_StatusText", "Bitte eine QUELLE wählen."); return; }
                if (string.IsNullOrWhiteSpace(targetDb)) { SetText("Copy_StatusText", "Bitte ein ZIEL wählen."); return; }
                if (string.Equals(sourceDb, targetDb, StringComparison.OrdinalIgnoreCase))
                { SetText("Copy_StatusText", "Quelle und Ziel dürfen nicht identisch sein."); return; }

                var opt = new DbCopyOptions
                {
                    CopyNumberRanges = (El<CheckBox>("Copy_CbNummernkreise")?.IsChecked == true),
                    CopyKontenstruktur = (El<CheckBox>("Copy_CbKonten")?.IsChecked == true),
                    CopyAdressen = (El<CheckBox>("Copy_CbAdressen")?.IsChecked == true),
                    CopyAliase = (El<CheckBox>("Copy_CbAliase")?.IsChecked == true),
                    CopyGeldinstitute = (El<CheckBox>("Copy_CbGeldinst")?.IsChecked == true),
                    CopyImportSchemas = (El<CheckBox>("Copy_CbImport")?.IsChecked == true),
                    CopyKategorieKonto = (El<CheckBox>("Copy_CbKatMap")?.IsChecked == true),
                    CreateBudgetzeitraum = (El<CheckBox>("Copy_CbBudget")?.IsChecked == true),
                    BudgetYear = DateTime.Today.Year
                };

                if (sender is Button b) b.IsEnabled = false;
                SetText("Copy_StatusText", $"Kopiere von '{sourceDb}' → '{targetDb}' …");

                await _copy.CopyAsync(sourceDb, targetDb, opt, createTargetIfMissing: false);

                SetText("Copy_StatusText", "Kopieren erfolgreich abgeschlossen.");
            }
            catch (Exception ex)
            {
                SetText("Copy_StatusText", "Fehler beim Kopieren: " + ex.Message);
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
            }
        }

        // ===== Backup / Restore =====
        private void SetBackupDefaultPath()
        {
            try
            {
                var dbName = ConnectionStrings.ActiveDatabaseName;
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyCoinFlow", "Backups");
                Directory.CreateDirectory(dir);
                var file = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmm}.bak";
                var box = El<TextBox>("BackupFileBox");
                if (box != null && string.IsNullOrWhiteSpace(box.Text))
                    box.Text = Path.Combine(dir, file);
                SetText("Backup_ActiveDbText", $"Aktive DB: {dbName}");
            }
            catch { }
        }

        private string GetBackupFileNameOrDefault(string currentPath)
        {
            var fileName = "";

            if (!string.IsNullOrWhiteSpace(currentPath))
                fileName = Path.GetFileName(currentPath);

            if (!string.IsNullOrWhiteSpace(fileName) &&
                fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            var dbName = ConnectionStrings.ActiveDatabaseName;
            return $"{dbName}_{DateTime.Now:yyyyMMdd_HHmm}.bak";
        }

        private static bool IsOneDrivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var fullPath = Path.GetFullPath(path);

            if (fullPath.Contains("\\OneDrive\\", StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains("\\OneDrive - ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var oneDriveVars = new[]
            {
        Environment.GetEnvironmentVariable("OneDrive"),
        Environment.GetEnvironmentVariable("OneDriveConsumer"),
        Environment.GetEnvironmentVariable("OneDriveCommercial")
    };

            foreach (var root in oneDriveVars)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var rootFull = Path.GetFullPath(root);

                if (fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }


        private void Backup_Browse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentPath = El<TextBox>("BackupFileBox")?.Text?.Trim() ?? "";
                var currentFileName = GetBackupFileNameOrDefault(currentPath);

                var initialDir = "";
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    var dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        initialDir = dir;
                }

                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Backup-Zielordner wählen",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    SelectedPath = initialDir
                };

                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var selectedDir = dlg.SelectedPath?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(selectedDir))
                    return;

                El<TextBox>("BackupFileBox")!.Text = Path.Combine(selectedDir, currentFileName);
                SetText("Backup_StatusText", "");
            }
            catch (Exception ex)
            {
                SetText("Backup_StatusText", "Zielordner konnte nicht gewählt werden: " + ex.Message);
            }
        }

        private async void Backup_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("Backup_StatusText", "");

                var db = ConnectionStrings.ActiveDatabaseName;
                var path = El<TextBox>("BackupFileBox")?.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(path))
                {
                    SetText("Backup_StatusText", "Bitte eine Zieldatei auswählen.");
                    return;
                }

                if (IsOneDrivePath(path))
                {
                    var msg =
                        "Backups in synchronisierte OneDrive-Ordner sind nicht erlaubt.\n\n" +
                        "Bitte wählen Sie einen lokalen Ordner, z. B. C:\\Backup oder D:\\MyCoinFlowBackup.";

                    SetText("Backup_StatusText", msg);

                    MessageBox.Show(
                        Window.GetWindow(this) ?? Application.Current.MainWindow,
                        msg,
                        "Backup-Ziel nicht erlaubt",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    SetText("Backup_StatusText", "Der Zielordner ist ungültig.");
                    return;
                }

                Directory.CreateDirectory(dir);

                var btn = sender as Button;
                if (btn != null) btn.IsEnabled = false;

                Mouse.OverrideCursor = Cursors.Wait;
                SetText("Backup_StatusText", $"Backup läuft… DB '{db}'. Bitte warten.");

                await _backup.BackupAsync(db, path, useCompression: true);

                SetText("Backup_StatusText", $"Backup erstellt: {path}");

                MessageBox.Show(
                    Window.GetWindow(this) ?? Application.Current.MainWindow,
                    $"Backup erstellt:\n{path}",
                    "Backup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetText("Backup_StatusText", "Backup-Fehler: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (sender is Button b) b.IsEnabled = true;
            }
        }

        private void Restore_BrowseBak_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Backup-Datei wählen",
                Filter = "SQL Server Backup (*.bak)|*.bak",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog() == true)
                El<TextBox>("Restore_FileBox")!.Text = ofd.FileName;
        }

        private async void Restore_Run_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;

            try
            {
                SetText("Restore_StatusText", "");
                var bak = El<TextBox>("Restore_FileBox")?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(bak))
                {
                    SetText("Restore_StatusText", "Bitte eine .bak-Datei wählen.");
                    return;
                }

                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                SetText("Restore_StatusText",
                    $"Restore läuft… Ziel: '{ConnectionStrings.ActiveDatabaseName}'. Bitte warten (kann mehrere Minuten dauern).");

                await _restore.RestoreActiveAsync(bak);

                SetText("Restore_StatusText",
                    $"Restore erfolgreich. Aktive DB '{ConnectionStrings.ActiveDatabaseName}' wurde überschrieben.");

                MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    $"Backup wurde in die aktive DB '{ConnectionStrings.ActiveDatabaseName}' zurückgespielt.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetText("Restore_StatusText", "Restore-Fehler: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        // ===== Benutzerverwaltung (aktive DB) =====

        private async Task LoadUsersAsync()
        {
            try
            {
                Users_ActiveDbText.Text = $"Aktive DB: {ConnectionStrings.ActiveDatabaseName}";
                Users_StatusText.Text = "";

                await _auth.EnsureSchemaAsync();
                var users = await _auth.GetUsersAsync();

                UsersGrid.ItemsSource = users;

                Users_StatusText.Text = users.Count == 0
                    ? "Noch keine Benutzer vorhanden. (Erster Benutzer wird beim Login angelegt.)"
                    : $"{users.Count} Benutzer geladen.";
            }
            catch (Exception ex)
            {
                Users_StatusText.Text = "Fehler beim Laden der Benutzer: " + ex.Message;
            }
        }

        private async void Users_Refresh_Click(object sender, RoutedEventArgs e)
            => await LoadUsersAsync();

        private async void Users_Add_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Users_StatusText.Text = "";

                var dlg = CreateAddUserDialog(out var tbUser, out var tbEmail, out var pw1, out var pw2, out var cbAdmin);
                var res = dlg.ShowDialog();
                if (res != true) return;

                var username = (tbUser.Text ?? "").Trim();
                var email = string.IsNullOrWhiteSpace(tbEmail.Text) ? null : tbEmail.Text.Trim();
                var p1 = pw1.Password ?? "";
                var p2 = pw2.Password ?? "";
                var isAdmin = cbAdmin.IsChecked == true;

                if (p1 != p2)
                {
                    Users_StatusText.Text = "Die Passwörter stimmen nicht überein.";
                    return;
                }

                await _auth.CreateUserAsync(username, p1, email, isAdmin);
                Users_StatusText.Text = $"Benutzer '{username}' wurde angelegt.";
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                Users_StatusText.Text = "Fehler: " + ex.Message;
            }
        }

        private async void Users_ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Users_StatusText.Text = "";

                if (UsersGrid.SelectedItem is not AuthService.UserRow u)
                {
                    Users_StatusText.Text = "Bitte zuerst einen Benutzer auswählen.";
                    return;
                }

                var dlg = CreateResetPasswordDialog(u.Username, out var pw1, out var pw2);
                var res = dlg.ShowDialog();
                if (res != true) return;

                var p1 = pw1.Password ?? "";
                var p2 = pw2.Password ?? "";
                if (p1 != p2)
                {
                    Users_StatusText.Text = "Die Passwörter stimmen nicht überein.";
                    return;
                }

                await _auth.ResetPasswordAsync(u.Id, p1);
                Users_StatusText.Text = $"Passwort für '{u.Username}' wurde zurückgesetzt.";
            }
            catch (Exception ex)
            {
                Users_StatusText.Text = "Fehler: " + ex.Message;
            }
        }

        private async void Users_ToggleActive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Users_StatusText.Text = "";

                if (UsersGrid.SelectedItem is not AuthService.UserRow u)
                {
                    Users_StatusText.Text = "Bitte zuerst einen Benutzer auswählen.";
                    return;
                }

                var newActive = !u.IsActive;
                await _auth.SetUserActiveAsync(u.Id, newActive);

                Users_StatusText.Text = newActive
                    ? $"Benutzer '{u.Username}' ist wieder aktiv."
                    : $"Benutzer '{u.Username}' wurde deaktiviert.";

                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                Users_StatusText.Text = "Fehler: " + ex.Message;
            }
        }

        private async void Users_ToggleAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Users_StatusText.Text = "";

                if (UsersGrid.SelectedItem is not AuthService.UserRow u)
                {
                    Users_StatusText.Text = "Bitte zuerst einen Benutzer auswählen.";
                    return;
                }

                var newAdmin = !u.IsAdmin;
                await _auth.SetUserAdminAsync(u.Id, newAdmin);

                Users_StatusText.Text = newAdmin
                    ? $"Benutzer '{u.Username}' ist jetzt Admin."
                    : $"Benutzer '{u.Username}' ist jetzt Standard-Benutzer.";

                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                Users_StatusText.Text = "Fehler: " + ex.Message;
            }
        }



        private static Window CreateAddUserDialog(
            out TextBox tbUser, out TextBox tbEmail, out PasswordBox pw1, out PasswordBox pw2, out CheckBox cbAdmin)
        {
            tbUser = new TextBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            tbEmail = new TextBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            pw1 = new PasswordBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            pw2 = new PasswordBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            cbAdmin = new CheckBox { Content = "Ist Admin", Margin = new Thickness(0, 0, 0, 8) };

            var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Abbrechen", Width = 90 };

            var grid = new StackPanel { Margin = new Thickness(12) };
            grid.Children.Add(new TextBlock { Text = "Username" });
            grid.Children.Add(tbUser);
            grid.Children.Add(new TextBlock { Text = "Email (optional)" });
            grid.Children.Add(tbEmail);
            grid.Children.Add(new TextBlock { Text = "Passwort" });
            grid.Children.Add(pw1);
            grid.Children.Add(new TextBlock { Text = "Passwort (wiederholen)" });
            grid.Children.Add(pw2);
            grid.Children.Add(cbAdmin);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.Children.Add(buttons);

            var w = new Window
            {
                Title = "Neuer Benutzer",
                Content = grid,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            ok.Click += (_, __) => { w.DialogResult = true; w.Close(); };
            cancel.Click += (_, __) => { w.DialogResult = false; w.Close(); };

            return w;
        }

        private static Window CreateResetPasswordDialog(string username, out PasswordBox pw1, out PasswordBox pw2)
        {
            pw1 = new PasswordBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            pw2 = new PasswordBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };

            var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Abbrechen", Width = 90 };

            var grid = new StackPanel { Margin = new Thickness(12) };
            grid.Children.Add(new TextBlock { Text = $"Passwort zurücksetzen für: {username}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            grid.Children.Add(new TextBlock { Text = "Neues Passwort" });
            grid.Children.Add(pw1);
            grid.Children.Add(new TextBlock { Text = "Neues Passwort (wiederholen)" });
            grid.Children.Add(pw2);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.Children.Add(buttons);

            var w = new Window
            {
                Title = "Passwort zurücksetzen",
                Content = grid,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            ok.Click += (_, __) => { w.DialogResult = true; w.Close(); };
            cancel.Click += (_, __) => { w.DialogResult = false; w.Close(); };

            return w;
        }

        private void ApplyRoleVisibility()
        {
            // Quelle: dein bestehender Status (aktuell via JSON True/False)
            // -> wir nutzen das, was du bereits hast (z. B. AppEdition/Config/CurrentUserContext).
            // Für jetzt: ConnectionStrings / Config ist egal, Hauptsache bool isAdmin ist korrekt.

            bool isAdmin = CurrentUserContext.IsAdmin;


            // NavList ist dein ListBox im XAML
            if (NavList == null) return;

            foreach (var item in NavList.Items.OfType<ListBoxItem>())
            {
                var tag = (item.Tag as string) ?? "";

                if (tag.Equals("Mandanten", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    item.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // Wenn Nicht-Admin gerade auf Mandanten/Update steht, automatisch auf Kontenplan wechseln
            if (!isAdmin)
            {
                var selected = NavList.SelectedItem as ListBoxItem;
                var selTag = (selected?.Tag as string) ?? "";

                if (selTag.Equals("Mandanten", StringComparison.OrdinalIgnoreCase) ||
                    selTag.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = NavList.Items.OfType<ListBoxItem>()
                        .FirstOrDefault(i => string.Equals(i.Tag as string, "Kontenplan", StringComparison.OrdinalIgnoreCase))
                        ?? NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Visibility == Visibility.Visible);

                    if (fallback != null)
                        NavList.SelectedItem = fallback;
                }
            }
        }

        private void License_Save_Click(object sender, RoutedEventArgs e)
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

                var lic = new LicenseService();

                if (!lic.TryValidate(key, out var payload, out var err))
                {
                    LicenseStatusText.Text = "Ungültige Lizenz: " + err;
                    return;
                }

                lic.SaveKey(key);

                // Info anzeigen – wirksam beim nächsten Start (wie gewünscht)
                LicenseStatusText.Text = "Lizenz gespeichert. Wirksam beim nächsten Start.";
                LicenseInfoText.Text =
                    $"Edition: {payload.Edition}\n" +
                    $"Kunde: {payload.Customer}\n" +
                    $"Ablauf: {(payload.ExpiresUtc.HasValue ? payload.ExpiresUtc.Value.ToString("yyyy-MM-dd") : "kein Ablauf")}";
            }
            catch (Exception ex)
            {
                LicenseStatusText.Text = "Fehler: " + ex.Message;
            }
        }

        private void KontenplanTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not TabControl tc) return;

                // Linker Actions-Host in der Sidebar
                var actionsHost = El<ContentControl>("KontenplanActionsHost")
                                  ?? FindByNameCaseInsensitive<ContentControl>(this, "KontenplanActionsHost");
                if (actionsHost == null) return;

                // Wir nehmen den DataContext aus der Liste (KontenArtListHost), das ist unser "Source of Truth"
                var artListHost = El<ContentControl>("KontenArtListHost")
                                  ?? FindByNameCaseInsensitive<ContentControl>(this, "KontenArtListHost");
                var vm = (artListHost?.Content as FrameworkElement)?.DataContext;

                // TabIndex: 0=Art, 1=Gruppe, 2=Untergruppe
                if (tc.SelectedIndex == 0)
                {
                    var v = TryCreateView("MyCoinFlow.Views.KontenArtActionsView");
                    if (v != null) v.DataContext = vm;   // ✅ wichtig!
                    actionsHost.Content = v;
                }
                else if (tc.SelectedIndex == 1)
                {
                    // DataContext aus Gruppen-Liste holen
                    var grpListHost = El<ContentControl>("KontenGruppeListHost")
                                      ?? FindByNameCaseInsensitive<ContentControl>(this, "KontenGruppeListHost");
                    var vm2 = (grpListHost?.Content as FrameworkElement)?.DataContext;

                    var v = TryCreateView("MyCoinFlow.Views.KontenGruppeActionsView");
                    if (v != null) v.DataContext = vm2;
                    actionsHost.Content = v;
                }
                else
                {
                    // DataContext aus Untergruppen-Liste holen
                    var ugListHost = El<ContentControl>("KontenUnterGruppeListHost")
                                     ?? FindByNameCaseInsensitive<ContentControl>(this, "KontenUnterGruppeListHost");
                    var vm3 = (ugListHost?.Content as FrameworkElement)?.DataContext;

                    var v = TryCreateView("MyCoinFlow.Views.KontenUnterGruppeActionsView");
                    if (v != null) v.DataContext = vm3;
                    actionsHost.Content = v;
                }

            }
            catch { }
        }

        private void MandantenTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not TabControl tc) return;

                // 0=Neu, 1=Copy, 2=Users
                MandantenActionsNeuPanel.Visibility = tc.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                MandantenActionsCopyPanel.Visibility = tc.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                MandantenActionsUsersPanel.Visibility = tc.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }
        private void BackupTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not TabControl tc) return;

                // 0=Backup, 1=Restore
                BackupActionsBackupPanel.Visibility = tc.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                BackupActionsRestorePanel.Visibility = tc.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private MyCoinFlow.Views.Admin.AdminUpdatesControl? GetUpdatesControl()
        {
            var host = El<ContentControl>("UpdatesHost") ?? FindByNameCaseInsensitive<ContentControl>(this, "UpdatesHost");
            return host?.Content as MyCoinFlow.Views.Admin.AdminUpdatesControl;
        }

        private void Updates_Search_Click(object sender, RoutedEventArgs e)
        {
            var uc = GetUpdatesControl();
            if (uc == null) return;

            var btn = FindByNameCaseInsensitive<Button>(uc, "CheckButton");
            btn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void Updates_Backup_Click(object sender, RoutedEventArgs e)
        {
            var uc = GetUpdatesControl();
            if (uc == null) return;

            var btn = FindByNameCaseInsensitive<Button>(uc, "BackupButton");
            btn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void Updates_Update_Click(object sender, RoutedEventArgs e)
        {
            var uc = GetUpdatesControl();
            if (uc == null) return;

            var btn = FindByNameCaseInsensitive<Button>(uc, "UpdateButton");
            btn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private AdminPathsView? GetPathsView()
        {
            var host = El<ContentControl>("PathsHost") ?? FindByNameCaseInsensitive<ContentControl>(this, "PathsHost");
            return host?.Content as AdminPathsView;
        }

        private void PathsTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not TabControl tc) return;

                PathsActionsAttachPanel.Visibility = tc.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                PathsActionsOcrPanel.Visibility = tc.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                PathsActionsDbPanel.Visibility = tc.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

                // rechts Panel umschalten
                GetPathsView()?.ShowSection(tc.SelectedIndex);
            }
            catch { }
        }

        private void Paths_CreateFolder_Click(object sender, RoutedEventArgs e) => GetPathsView()?.CreateFolder_Click(sender, e);
        private void Paths_OpenExplorer_Click(object sender, RoutedEventArgs e) => GetPathsView()?.OpenInExplorer_Click(sender, e);
        private void Paths_Save_Click(object sender, RoutedEventArgs e) => GetPathsView()?.Save_Click(sender, e);
        private void Paths_BrowseTesseract_Click(object sender, RoutedEventArgs e) => GetPathsView()?.BrowseTesseract_Click(sender, e);
        private void Paths_SaveOcr_Click(object sender, RoutedEventArgs e) => GetPathsView()?.SaveOcr_Click(sender, e);
        private void Paths_Reindex_Click(object sender, RoutedEventArgs e) => GetPathsView()?.Reindex_Click(sender, e);
        private void Paths_RefreshDb_Click(object sender, RoutedEventArgs e) => GetPathsView()?.RefreshDbStats_Click(sender, e);


    }
}
