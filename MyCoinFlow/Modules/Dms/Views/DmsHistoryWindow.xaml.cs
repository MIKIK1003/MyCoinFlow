using System;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;

namespace MyCoinFlow.Views
{
    public partial class DmsHistoryWindow : BaseWindow
    {
        private static DmsHistoryWindow? _open;

        /// <summary>
        /// Öffnet die Historie nicht-modal; ist bereits eine Instanz offen, wird diese
        /// nur in den Vordergrund geholt (kein Duplikat), damit das Fenster wie gewünscht
        /// unabhängig von der DMS-Ansicht offen bleiben kann.
        /// </summary>
        public static void ShowOrActivate(System.Windows.Window? owner)
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }

            _open = new DmsHistoryWindow { Owner = owner };
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }

        private readonly DatabaseService _db = new();

        public DmsHistoryWindow()
        {
            InitializeComponent();

            DmsWatcherService.Instance.DocumentProcessed += Watcher_DocumentProcessed;
            Closed += (_, _) => DmsWatcherService.Instance.DocumentProcessed -= Watcher_DocumentProcessed;

            Laden();
        }

        private void Watcher_DocumentProcessed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(Laden);
        }

        private void AktualisierenButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Laden();
        }

        private void Laden()
        {
            HistorieGrid.ItemsSource = _db.LoadDmsProcessingLog();
        }
    }
}
