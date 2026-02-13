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
using MyCoinFlow.Models;          // für ImportSchema
using System.Windows.Input;


namespace MyCoinFlow.Views
{
    public partial class AdminView : UserControl
    {
        private readonly DbCopyService _copy = new();
        private readonly DbBackupService _backup = new();
        private readonly DbRestoreService _restore = new();
        private readonly DatabaseService _dbSvc = new();   // für ImportSchema-Operationen

        // NEU: Mandanten-Service (arbeitet über ConnectionStrings.Master -> .\SQLEXPRESS)
        private readonly MandantService _mandants = new();

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

        private async Task LoadCreditCardSchemasAsync()
        {
            try
            {
                // alle Schemas, Master ausblenden (nur benutzerdefinierte sind löschbar)
                var all = _dbSvc.ImportSchemasGetAll();  // liefert Id, Name, IsMaster
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
            try { new DatabaseService().EnsureNumberRangeRulesTable(); } catch { /* still */ }

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

            // Hosts füllen (ohne <local:...> im XAML)
            EnsureKontenHosts();
            EnsureCreditCardMappingInline();
            AttachNumberRangesView();
            EnsureUpdatesHost();
            EnsurePathsHost();

            await LoadCreditCardSchemasAsync();
            await LoadCopyCombosAsync();
            SetBackupDefaultPath();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = ((El<ListBox>("NavList")?.SelectedItem as ListBoxItem)?.Tag as string) ?? "Kontenplan";
            ShowSection(tag);
            if (string.Equals(tag, "Backup", StringComparison.OrdinalIgnoreCase))
                SetBackupDefaultPath();

            // DB-Combos aktualisieren, wenn man zu Mandanten wechselt
            if (string.Equals(tag, "Mandanten", StringComparison.OrdinalIgnoreCase))
                _ = LoadCopyCombosAsync();
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

            SetVis("SecNummernkreise", isNum);
            SetVis("SecKontenplan", isKonten);
            SetVis("SecKreditkarten", isKredit);
            SetVis("SecMandanten", isMand);
            SetVis("SecUpdate", isUpdate);
            SetVis("SecBackup", isBackup);
            SetVis("SecPfade", isPfade);

            if (isKredit) EnsureCreditCardMappingInline();
            if (isPfade) EnsurePathsHost();
        }

        // ===== Nummernkreise-Host =====
        private void AttachNumberRangesView()
        {
            try
            {
                SetHostIfEmpty("NumberRangesHost", () => TryCreateView("MyCoinFlow.Views.AdminNumberRangesView"));
            }
            catch { /* still */ }
        }

        // ===== Kontenplan: Hosts / Import / Export =====
        private void EnsureKontenHosts()
        {
            try { SetHostIfEmpty("KontenArtHost", () => TryCreateView("MyCoinFlow.Views.KontenArtView")); } catch { }
            try { SetHostIfEmpty("KontenGruppeHost", () => TryCreateView("MyCoinFlow.Views.KontenGruppeView")); } catch { }
            try { SetHostIfEmpty("KontenUnterGruppeHost", () => TryCreateView("MyCoinFlow.Views.KontenUnterGruppeView")); } catch { }
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
                    if (owner != null)
                        dlg.Owner = owner;
                    else
                        dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

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
                        if (owner != null)
                            host.Owner = owner;
                        else
                            host.WindowStartupLocation = WindowStartupLocation.CenterScreen;

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

        private async void CC_SchemaRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadCreditCardSchemasAsync();
        }

        private async void CC_SchemaDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cb = El<ComboBox>("CC_SchemaCombo");
                if (cb?.SelectedItem is not ImportSchema sel)
                {
                    MessageBox.Show("Bitte zuerst ein Schema wählen.", "Löschen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (sel.IsMaster)
                {
                    MessageBox.Show("Das Master-Schema kann nicht gelöscht werden.", "Löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var ask = MessageBox.Show($"Schema „{sel.Name}“ wirklich löschen?", "Löschen bestätigen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (ask != MessageBoxResult.Yes) return;

                _dbSvc.ImportSchemaDelete(sel.Id);

                await LoadCreditCardSchemasAsync();
                MessageBox.Show("Schema wurde gelöscht.", "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Löschen fehlgeschlagen:\n" + ex.Message, "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void OpenCreditCardMapping_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var view = new CreditCardImportMappingView();

                var host = new Window
                {
                    Title = "Kreditkarten-Mapping",
                    Content = view,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current.MainWindow
                };
                host.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Mapping konnte nicht geöffnet werden:\n" + ex.Message,
                                "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== Updates-Host =====
        private void EnsureUpdatesHost()
        {
            try
            {
                SetHostIfEmpty("UpdatesHost", () => new MyCoinFlow.Views.Admin.AdminUpdatesControl());
            }
            catch { }
        }

        // ===== Mandanten: neue leere DB (NEU: Template .bak, kein LocalDB, keine Quelle-DB) =====
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
            }
            catch (Exception ex)
            {
                SetText("TenantCreateStatus", "Fehler: " + ex.Message);
            }
        }

        // ===== DB-Kopie: DB-Listen (NEU: aus ConnectionStrings.Master => SQLEXPRESS) =====
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

        private void Backup_Browse_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Ziel-Datei (*.bak)",
                Filter = "SQL Server Backup (*.bak)|*.bak",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() == true)
                El<TextBox>("BackupFileBox")!.Text = sfd.FileName;
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

                await _backup.BackupAsync(db, path, useCompression: true);

                MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    $"Backup erstellt:\n{path}", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetText("Backup_StatusText", "Backup-Fehler: " + ex.Message);
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

                // Visuelles Feedback
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                SetText("Restore_StatusText",
                    $"Restore läuft… Ziel: '{ConnectionStrings.ActiveDatabaseName}'. Bitte warten (kann mehrere Minuten dauern).");

                // Restore ausführen
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


        private void EnsurePathsHost()
        {
            try
            {
                SetHostIfEmpty("PathsHost", () => new MyCoinFlow.Views.AdminPathsView());
            }
            catch { }
        }
    }
}
