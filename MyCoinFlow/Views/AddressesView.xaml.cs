using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Interaktionslogik für AddressesView.xaml
    /// </summary>
    public partial class AddressesView : UserControl
    {
        public AddressesView()
        {
            InitializeComponent();
            Loaded += AddressesView_Loaded;   // << Filter beim Laden anhängen
        }

        private void AddressesView_Loaded(object sender, RoutedEventArgs e)
        {
            // Filter an die aktuelle ItemsSource hängen (nur 1x)
            var view = CollectionViewSource.GetDefaultView(AddressesGrid?.ItemsSource);
            if (view != null && view.Filter == null)
                view.Filter = RowFilter;
        }

        // --- Buttons ---

        private void ApplySearch_Click(object sender, RoutedEventArgs e)
        {
            // Falls dein VM ein ApplySearchCommand hat → nutzen
            TryExecVmCommand("ApplySearchCommand");
            // In jedem Fall: View neu auswerten (RowFilter liest SearchBox)
            RefreshView();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            // Suchfeld leeren
            if (SearchBox != null) SearchBox.Text = string.Empty;

            // Optional: VM-Command ausführen, falls vorhanden
            TryExecVmCommand("ClearSearchCommand");

            // Anzeige aktualisieren
            RefreshView();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryExecVmCommand("ApplySearchCommand");
                RefreshView();
                e.Handled = true;
            }
        }

        // --- Helfer ---

        private void RefreshView()
        {
            var view = CollectionViewSource.GetDefaultView(AddressesGrid?.ItemsSource);
            view?.Refresh();
        }

        private void TryExecVmCommand(string commandPropertyName)
        {
            if (DataContext == null) return;
            var p = DataContext.GetType().GetProperty(commandPropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (p?.GetValue(DataContext) is ICommand cmd && cmd.CanExecute(null))
                cmd.Execute(null);
        }

        // --- Filterlogik: alle Tokens müssen in irgendeinem Feld vorkommen (CI) ---

        private bool RowFilter(object o)
        {
            if (o == null) return false;

            string GetStr(string prop)
            {
                var pi = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var v = pi?.GetValue(o);
                return v?.ToString() ?? string.Empty;
            }

            var q = (SearchBox?.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q)) return true; // kein Suchtext -> alles zeigen

            var tokens = q.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var t in tokens)
            {
                var tok = t.ToLowerInvariant();
                bool hit =
                    GetStr("Name").ToLowerInvariant().Contains(tok) ||
                    GetStr("Strasse").ToLowerInvariant().Contains(tok) ||
                    GetStr("PLZ").ToLowerInvariant().Contains(tok) ||
                    GetStr("Ort").ToLowerInvariant().Contains(tok) ||
                    GetStr("Land").ToLowerInvariant().Contains(tok) ||
                    GetStr("Typ").ToLowerInvariant().Contains(tok) ||
                    GetStr("IBAN").ToLowerInvariant().Contains(tok) ||
                    GetStr("Notiz").ToLowerInvariant().Contains(tok);

                if (!hit) return false; // sobald ein Token nicht passt → Zeile raus
            }
            return true;
        }
    }
}
