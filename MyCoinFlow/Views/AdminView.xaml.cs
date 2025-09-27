using Microsoft.Win32;
using MyCoinFlow.Services;
using MyCoinFlow.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class AdminView : UserControl
    {
        private readonly MandantService _mandanten = new();
        private readonly DbCopyService _copy = new();
        private readonly DbProvisioner _prov = new();
        private readonly DbBackupService _backup = new();
        private readonly DbRestoreService _restore = new();

        public AdminView()
        {
            InitializeComponent();
            Loaded += AdminView_Loaded;
        }

        // Helper
        private T? El<T>(string name) where T : FrameworkElement => FindName(name) as T;
        private void SetText(string name, string txt) { var tb = El<TextBlock>(name); if (tb != null) tb.Text = txt ?? ""; }

        private async void AdminView_Loaded(object sender, RoutedEventArgs e)
        {
            // Navigation initial
            var nav = El<ListBox>("NavList");
            if (nav != null)
            {
                nav.SelectionChanged -= NavList_SelectionChanged;
                nav.SelectionChanged += NavList_SelectionChanged;
                nav.SelectedIndex = 0;
            }
            ShowSection("Kontenplan");

            // bestehende Hosts (Konten)
            EnsureKontenHosts();
            // Kreditkarten-Host
            EnsureCreditCardMappingInline();

            // Mandanten-Combos für DB-Kopie
            await LoadCopyCombosAsync();

            // Backup-Text/Default
            SetBackupDefaultPath();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = ((El<ListBox>("NavList")?.SelectedItem as ListBoxItem)?.Tag as string) ?? "Kontenplan";
            ShowSection(tag);
            if (string.Equals(tag, "Backup", StringComparison.OrdinalIgnoreCase))
                SetBackupDefaultPath();
        }

        private void ShowSection(string key)
        {
            var konten = El<Grid>("SecKontenplan");
            var kredit = El<Grid>("SecKreditkarten");
            var mandant = El<Grid>("SecMandanten");
            var backup = El<Grid>("SecBackup");

            if (konten != null) konten.Visibility = key == "Kontenplan" ? Visibility.Visible : Visibility.Collapsed;
            if (kredit != null) kredit.Visibility = key == "Kreditkarten" ? Visibility.Visible : Visibility.Collapsed;
            if (mandant != null) mandant.Visibility = key == "Mandanten" ? Visibility.Visible : Visibility.Collapsed;
            if (backup != null) backup.Visibility = key == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- bestehende Funktionen: Kontenplan Import/Export ---
        private void ImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new KontenplanImportDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
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
                    var exporter = new Importing.KontenplanExcelExporter();
                    exporter.Export(sfd.FileName);
                    MessageBox.Show("Kontenplan wurde exportiert.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export fehlgeschlagen:\n" + ex.Message,
                        "Export", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // --- Kreditkarten Mapping Dialog ---
        private void OpenCreditCardImportMappingDialog()
        {
            var repo = new DatabaseService();
            var svc = new CreditCardImportMappingService(repo);
            var view = new CreditCardImportMappingView
            {
                DataContext = new CreditCardImportMappingViewModel(svc)
            };
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

        private void OpenCreditCardMapping_Click(object sender, RoutedEventArgs e) => OpenCreditCardImportMappingDialog();

        // --- Mandant (leer) anlegen ---
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

                await _prov.CreateDatabaseAsync(name, null);
                await _prov.CloneSchemaFromTemplateAsync("MyCoinFlowDB", name);

                SetText("TenantCreateStatus", $"Mandant '{name}' erstellt.");
                await LoadCopyCombosAsync();
            }
            catch (Exception ex)
            {
                SetText("TenantCreateStatus", "Fehler: " + ex.Message);
            }
        }

        // --- DB-Kopie (Quelle -> Ziel) ---
        private async Task LoadCopyCombosAsync()
        {
            try
            {
                SetText("Copy_StatusText", "");
                var list = await _mandanten.GetMandantenAsync();

                var src = El<ComboBox>("Copy_SourceDbCombo");
                var dst = El<ComboBox>("Copy_TargetDbCombo");

                if (src != null) { src.ItemsSource = list.ToList(); }
                if (dst != null) { dst.ItemsSource = list.ToList(); }

                var active = ConnectionStrings.ActiveDatabaseName;
                if (list.Contains(active))
                {
                    if (src != null) src.SelectedItem = active;
                    if (dst != null) dst.SelectedItem = active;
                }
                else
                {
                    if (src != null) src.SelectedIndex = list.Count > 0 ? 0 : -1;
                    if (dst != null) dst.SelectedIndex = list.Count > 0 ? 0 : -1;
                }
            }
            catch (Exception ex)
            {
                SetText("Copy_StatusText", "Fehler beim Laden: " + ex.Message);
            }
        }

        private async void Copy_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("Copy_StatusText", "");

                var srcCmb = El<ComboBox>("Copy_SourceDbCombo");
                var dstCmb = El<ComboBox>("Copy_TargetDbCombo");

                var src = (srcCmb?.SelectedItem as string) ?? srcCmb?.Text?.Trim();
                var dst = dstCmb?.SelectedItem as string;

                if (string.IsNullOrWhiteSpace(src))
                {
                    SetText("Copy_StatusText", "Bitte Quelle wählen.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(dst))
                {
                    SetText("Copy_StatusText", "Bitte Ziel wählen.");
                    return;
                }

                var opt = new DbCopyOptions
                {
                    CopyKontenstruktur = (El<CheckBox>("Copy_CbKonten")?.IsChecked == true),
                    CopyAdressen = (El<CheckBox>("Copy_CbAdressen")?.IsChecked == true),
                    CopyAliase = (El<CheckBox>("Copy_CbAliase")?.IsChecked == true),
                    CopyGeldinstitute = (El<CheckBox>("Copy_CbGeldinst")?.IsChecked == true),
                    CopyImportSchemas = (El<CheckBox>("Copy_CbImport")?.IsChecked == true),
                    CopyKategorieKonto = (El<CheckBox>("Copy_CbKatMap")?.IsChecked == true),
                    CreateBudgetzeitraum = (El<CheckBox>("Copy_CbBudget")?.IsChecked == true),
                    BudgetYear = DateTime.Today.Year
                };

                await _copy.CopyAsync(src!, dst!, opt, createTargetIfMissing: false);

                MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    $"Kopieren abgeschlossen.\nQuelle: {src}\nZiel: {dst}",
                    "DB-Kopie", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetText("Copy_StatusText", "Fehler: " + ex.Message);
            }
        }

        // --- Backup / Restore ---
        private void SetBackupDefaultPath()
        {
            var db = ConnectionStrings.ActiveDatabaseName;
            SetText("Backup_ActiveDbText", $"Aktive DB: {db}");
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var defaultName = $"{db}_{ts}.bak";
            var box = El<TextBox>("BackupFileBox");
            if (box != null)
            {
                box.Text = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    defaultName);
            }
        }

        private void Backup_Browse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var db = ConnectionStrings.ActiveDatabaseName;
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var sfd = new SaveFileDialog
                {
                    Title = $"Backup – {db}",
                    Filter = "SQL Server Backup (*.bak)|*.bak",
                    FileName = $"{db}_{ts}.bak",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (sfd.ShowDialog() == true)
                {
                    var box = El<TextBox>("BackupFileBox");
                    if (box != null) box.Text = sfd.FileName;
                }
            }
            catch (Exception ex)
            {
                SetText("Backup_StatusText", "Fehler beim Auswählen: " + ex.Message);
            }
        }

        private async void Backup_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("Backup_StatusText", "");
                var db = ConnectionStrings.ActiveDatabaseName;
                var box = El<TextBox>("BackupFileBox");
                var path = box?.Text?.Trim() ?? "";

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
            try
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Backup-Datei wählen",
                    Filter = "SQL Server Backup (*.bak)|*.bak",
                    CheckFileExists = true
                };
                if (ofd.ShowDialog() == true)
                {
                    var box = El<TextBox>("Restore_FileBox");
                    if (box != null) box.Text = ofd.FileName;
                }
            }
            catch (Exception ex)
            {
                SetText("Restore_StatusText", "Fehler beim Auswählen: " + ex.Message);
            }
        }

        private async void Restore_Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetText("Restore_StatusText", "");
                var bak = El<TextBox>("Restore_FileBox")?.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(bak))
                {
                    SetText("Restore_StatusText", "Bitte eine .bak-Datei wählen.");
                    return;
                }

                await _restore.RestoreActiveAsync(bak);

                MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    $"Backup wurde in die aktive DB '{ConnectionStrings.ActiveDatabaseName}' zurückgespielt.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetText("Restore_StatusText", "Restore-Fehler: " + ex.Message);
            }
        }

        // --- Hosts für bestehende Bereiche ---
        private void EnsureKontenHosts()
        {
            SetHostIfEmpty("KontenArtHost", () => new KontenArtView());
            SetHostIfEmpty("KontenGruppeHost", () => new KontenGruppeView());
            SetHostIfEmpty("KontenUnterGruppeHost", () => new KontenUnterGruppeView());
        }

        private void EnsureCreditCardMappingInline()
        {
            var host = El<ContentControl>("CreditCardImportMappingHost");
            if (host != null && host.Content == null)
            {
                var view = new CreditCardImportMappingView();
                if (view.DataContext == null)
                {
                    var repo = new DatabaseService();
                    var svc = new CreditCardImportMappingService(repo);
                    view.DataContext = new CreditCardImportMappingViewModel(svc);
                }
                host.Content = view;
            }
        }

        private void SetHostIfEmpty(string hostName, Func<FrameworkElement> createView)
        {
            var host = El<ContentControl>(hostName);
            if (host != null && host.Content == null)
                host.Content = createView();
        }
    }
}
