using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MyCoinFlow.Models;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AccountsView : UserControl
    {
        private readonly DatabaseService _db = new();

        public AccountsView()
        {
            InitializeComponent();
            // wie in deiner funktionierenden Version: VM zuweisen
            this.DataContext = new AccountsViewModel(); // :contentReference[oaicite:1]{index=1}
            this.Loaded += AccountsView_Loaded;
        }

        private void AccountsView_Loaded(object sender, RoutedEventArgs e)
        {
            AttachGridFilter();
            RefreshGridView();
        }

        // ========== Filter an Grid-View hängen ==========
        private void AttachGridFilter()
        {
            if (AccountsGrid?.ItemsSource == null) return;
            var view = CollectionViewSource.GetDefaultView(AccountsGrid.ItemsSource);
            if (view != null) view.Filter = GridRowFilter;
        }

        private void RefreshGridView()
        {
            var view = CollectionViewSource.GetDefaultView(AccountsGrid?.ItemsSource);
            view?.Refresh();
        }

        // ========== Filter-Events ==========
        private void ApplySearch_Click(object sender, RoutedEventArgs e)
        {
            RefreshGridView();
            SelectTreeNodeFromKontoCombo(); // „Filter auf Baum ausdehnen“: passenden Knoten selektieren
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;
            if (DateFromPicker != null) DateFromPicker.SelectedDate = null;
            if (DateToPicker != null) DateToPicker.SelectedDate = null;
            if (KontoCombo != null) KontoCombo.SelectedItem = null;

            RefreshGridView();
        }

        private void Filter_TextChanged(object sender, TextChangedEventArgs e) => RefreshGridView();
        private void Filter_DateChanged(object sender, SelectionChangedEventArgs e) => RefreshGridView();
        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshGridView();
            SelectTreeNodeFromKontoCombo();
        }

        // ========== Grid-Filterlogik ==========
        private bool GridRowFilter(object obj)
        {
            if (obj == null) return false;

            string GetStr(string prop)
            {
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var v = pi?.GetValue(obj);
                return v?.ToString() ?? string.Empty;
            }
            int GetInt(string prop)
            {
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var v = pi?.GetValue(obj);
                if (v == null) return 0;
                try { return Convert.ToInt32(v); } catch { return 0; }
            }

            // 1) Text (alle Tokens müssen treffen)
            var q = (SearchBox?.Text ?? string.Empty).Trim();
            var tokens = q.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim().ToLowerInvariant())
                          .ToArray();

            if (tokens.Length > 0)
            {
                foreach (var t in tokens)
                {
                    bool hit =
                        GetStr("Detail").ToLowerInvariant().Contains(t) ||
                        GetStr("Art").ToLowerInvariant().Contains(t) ||
                        GetStr("Gruppe").ToLowerInvariant().Contains(t) ||
                        GetStr("Untergruppe").ToLowerInvariant().Contains(t) ||
                        GetStr("Kontonummer").ToLowerInvariant().Contains(t);

                    if (!hit) return false;
                }
            }

            // 2) Kontoauswahl
            int selectedKnr = 0;
            if (KontoCombo?.SelectedItem != null)
            {
                var pi = KontoCombo.SelectedItem.GetType().GetProperty("Kontonummer");
                var v = pi?.GetValue(KontoCombo.SelectedItem);
                if (v != null) int.TryParse(v.ToString(), out selectedKnr);
            }
            if (selectedKnr > 0)
            {
                int rowKnr = GetInt("Kontonummer");
                if (rowKnr != selectedKnr) return false;
            }

            // 3) Datum: nur Konten mit Buchungen im Zeitraum
            DateTime? von = DateFromPicker?.SelectedDate;
            DateTime? bis = DateToPicker?.SelectedDate;
            if (von.HasValue || bis.HasValue)
            {
                int knr = GetInt("Kontonummer");
                if (knr > 0)
                {
                    bool hasTx = _db.KontoHatBuchungenImZeitraumByKontonummer(knr, von, bis);
                    if (!hasTx) return false;
                }
            }

            return true;
        }

        // ========== Baum-Unterstützung ==========
        /// <summary>
        /// Wählt im TreeView den Knoten der gewählten Kontonummer (falls vorhanden).
        /// „Filterwirkung“ auf den Baum, ohne die Hierarchie zu zerstören.
        /// </summary>
        private void SelectTreeNodeFromKontoCombo()
        {
            if (KontoCombo?.SelectedItem == null || AccountsTree == null) return;

            // Kontonummer extrahieren
            int selectedKnr = 0;
            var pi = KontoCombo.SelectedItem.GetType().GetProperty("Kontonummer");
            var v = pi?.GetValue(KontoCombo.SelectedItem);
            if (v != null) int.TryParse(v.ToString(), out selectedKnr);
            if (selectedKnr <= 0) return;

            // Suche im Baum über Header-Text (AnzeigeText enthält i.d.R. Nummer/Detail)
            foreach (var root in AccountsTree.Items)
            {
                var tvi = AccountsTree.ItemContainerGenerator.ContainerFromItem(root) as TreeViewItem
                          ?? GetTreeViewItemRecursive(AccountsTree, root);
                if (tvi == null) continue;

                if (TrySelectKontoNode(tvi, selectedKnr))
                    break;
            }
        }

        private static TreeViewItem? GetTreeViewItemRecursive(ItemsControl parent, object item)
        {
            var tvi = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (tvi != null) return tvi;

            for (int i = 0; i < parent.Items.Count; i++)
            {
                var child = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (child == null) continue;
                child.ApplyTemplate();
                var presenter = FindVisualChild<ItemsPresenter>(child);
                if (presenter == null)
                {
                    child.UpdateLayout();
                    presenter = FindVisualChild<ItemsPresenter>(child);
                }
                var found = GetTreeViewItemRecursive(child, item);
                if (found != null) return found;
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                var sub = FindVisualChild<T>(child);
                if (sub != null) return sub;
            }
            return null;
        }

        private bool TrySelectKontoNode(TreeViewItem node, int kontonummer)
        {
            // Header-Text prüfen
            string headerText = "";
            if (node.Header is TextBlock tb) headerText = tb.Text ?? "";
            else headerText = node.Header?.ToString() ?? "";

            if (headerText.Contains(kontonummer.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                node.IsSelected = true;
                node.BringIntoView();
                return true;
            }

            // Kinder rekursiv durchsuchen
            node.IsExpanded = true;
            for (int i = 0; i < node.Items.Count; i++)
            {
                var childItem = node.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (childItem == null)
                {
                    // erzwingen
                    node.UpdateLayout();
                    childItem = node.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                }
                if (childItem != null && TrySelectKontoNode(childItem, kontonummer))
                    return true;
            }
            node.IsExpanded = false;
            return false;
        }

        // ========== Auswahl-Fix für „Bearbeiten“ ==========
        private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // wenn VM eine Ausgewählt-Property hat, mit der Tabellenzeile füttern
            var row = AccountsGrid?.SelectedItem;
            if (row == null) return;

            var vm = this.DataContext;
            if (vm == null) return;

            // Versuchsreihe gängiger Property-Namen
            var candidateProps = new[] { "AusgewaehlterEintrag", "AusgewaehlterKontoplanEintrag", "AusgewaehlterKnoten" };
            foreach (var name in candidateProps)
            {
                var p = vm.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(row.GetType()))
                {
                    p.SetValue(vm, row);
                    break;
                }
            }
        }

        // ========== bestehende Handler aus deiner Version ==========
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (this.DataContext is AccountsViewModel vm && e.NewValue is KontoplanKnoten knoten)
            {
                vm.AusgewaehlterKnoten = knoten;
            }
        }

        private void OpenKontoTransaktionen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is KontoplanEintrag row)
            {
                int kontoId = row.Id;
                string name = !string.IsNullOrWhiteSpace(row.Detail)
                              ? row.Detail
                              : (row.Kontonummer > 0 ? $"Konto {row.Kontonummer}" : $"Konto #{kontoId}");

                var wnd = new KontoTransaktionenWindow(kontoId, name)
                {
                    Owner = Application.Current.MainWindow
                };
                wnd.ShowDialog();
            }
        }
    }
}
