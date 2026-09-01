using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Import;
using MyCoinFlow.ViewModels;
using System.Collections.ObjectModel;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class BankImportWindow : PersistentWindow
{
    private readonly BankImportViewModel _viewModel = new();
    private BookingAssignmentWindow? _assignmentWindow;

    public BankImportWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1500, 880));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1120;
            presenter.PreferredMinimumHeight = 680;
        }

        RowsList.ItemsSource = VisibleItems;
        RefreshView();
    }

    public ObservableCollection<BankImportItem> VisibleItems { get; } = new();
    public bool Changed { get; private set; }

    private void OnOpenCamtClick(object sender, RoutedEventArgs e) => ExecuteAndRefresh(_viewModel.OpenFileCommand);
    private void OnClearClick(object sender, RoutedEventArgs e) => ExecuteAndRefresh(_viewModel.ClearCommand);
    private void OnSaveClick(object sender, RoutedEventArgs e) => ExecuteAndRefresh(_viewModel.SaveToDbCommand);
    private void OnLoadClick(object sender, RoutedEventArgs e) => ExecuteAndRefresh(_viewModel.LoadPendingFromDbCommand);

    private void OnBulkClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.BulkUebernehmenCommand.CanExecute(null))
        {
            ShowStatus("Es sind keine verbuchbaren, vollständig zugeordneten Staging-Zeilen vorhanden.", InfoBarSeverity.Warning);
            return;
        }

        Changed = true;
        ExecuteAndRefresh(_viewModel.BulkUebernehmenCommand);
    }

    private void OnIncompleteOnlyClick(object sender, RoutedEventArgs e)
    {
        _viewModel.OnlyIncomplete = IncompleteOnlyCheck.IsChecked == true;
        RefreshView();
    }

    private void OnAssignRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BankImportItem item) return;
        if (_assignmentWindow is not null)
        {
            _assignmentWindow.Activate();
            return;
        }

        try
        {
            var window = new BookingAssignmentWindow(item);
            _assignmentWindow = window;
            window.AssignmentCompleted += (_, result) =>
            {
                try
                {
                    _viewModel.UebernehmeZuordnung(item, result.AddressId, result.AccountId);
                    RefreshView();
                    ShowStatus("Zuordnung übernommen und Regeln neu ausgewertet.", InfoBarSeverity.Success);
                }
                catch (Exception exception)
                {
                    ShowStatus("Anlernen fehlgeschlagen: " + exception.Message, InfoBarSeverity.Error);
                }
            };
            window.Closed += (_, _) => _assignmentWindow = null;
            window.Activate();
        }
        catch (Exception exception)
        {
            ShowStatus("Zuordnung konnte nicht geöffnet werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnBookRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BankImportItem item) return;
        if (!_viewModel.EinzelBuchenCommand.CanExecute(item)) return;

        Changed = true;
        _viewModel.EinzelBuchenCommand.Execute(item);
        RefreshView();
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BankImportItem item) return;
        _viewModel.DeleteImportedRowCommand.Execute(item);
        RefreshView();
    }

    private void ExecuteAndRefresh(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
        RefreshView();
    }

    private void RefreshView()
    {
        VisibleItems.Clear();
        foreach (var item in _viewModel.Items.Where(item => !_viewModel.OnlyIncomplete || !item.IstVollstaendig))
            VisibleItems.Add(item);

        SourceText.Text = string.IsNullOrWhiteSpace(_viewModel.FilePath)
            ? "Quelle: (DB oder Datei)"
            : "Quelle: " + _viewModel.FilePath;
        CountText.Text = $"{VisibleItems.Count} von {_viewModel.Items.Count} Einträgen";
        StatusBar.Message = _viewModel.Items.Count == 0
            ? "Bitte eine CAMT-Datei wählen oder offene Staging-Zeilen laden."
            : $"{_viewModel.Items.Count} Importzeilen · {_viewModel.Items.Count(item => item.IstVollstaendig)} erkannt · " +
              $"{_viewModel.Items.Count(item => item.StagingId.HasValue)} gespeichert";
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.IsOpen = true;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
