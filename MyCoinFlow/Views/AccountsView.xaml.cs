using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
            // VM wie in deiner funktionierenden Version
            this.DataContext = new AccountsViewModel(); // VM-Zuweisung beibehalten
            this.Loaded += AccountsView_Loaded;
        }

        private void AccountsView_Loaded(object? sender, RoutedEventArgs e)
        {
            AttachGridFilter();
            // Beim ersten Wechsel in die Tabellenansicht ggf. erste Zeile selektieren,
            // damit Bearbeiten/Löschen nicht deaktiviert bleiben.
            if (AccountsGrid.Items.Count > 0 && AccountsGrid.SelectedItem == null)
            {
                AccountsGrid.SelectedIndex = 0;
            }
            RefreshGridView();
        }

        // -------- Filter an Grid-View hängen --------
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

        // -------- Filter-Events --------
        private void ApplySearch_Click(object sender, RoutedEventArgs e)
        {
            RefreshGridView();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;
            if (DateFromPicker != null) DateFromPicker.SelectedDate = null;
            if (DateToPicker != null) DateToPicker.SelectedDate = null;
            if (KontoCombo != null) KontoCombo.SelectedItem = null;

            // Optional: erste Zeile wieder wählen
            if (AccountsGrid.Items.Count > 0)
                AccountsGrid.SelectedIndex = 0;

            RefreshGridView();
            CommandManager.InvalidateRequerySuggested();
        }

        private void Filter_TextChanged(object sender, TextChangedEventArgs e) => RefreshGridView();
        private void Filter_DateChanged(object sender, SelectionChangedEventArgs e) => RefreshGridView();
        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshGridView();

        // -------- Grid-Filterlogik --------
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

        // -------- Auswahl-Fix: markierte Tabellenzeile ins VM spiegeln --------
        private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = AccountsGrid?.SelectedItem;
            if (row == null) return;

            // 1) Versuche AusgewaehlterEintrag (typisch für Tabellenzeilen)
            var vm = this.DataContext;
            if (vm != null)
            {
                var pEntry = vm.GetType().GetProperty("AusgewaehlterEintrag",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pEntry != null && pEntry.CanWrite && pEntry.PropertyType.IsInstanceOfType(row))
                {
                    pEntry.SetValue(vm, row);
                }
                else
                {
                    // 2) Fallback: AusgewaehlterKnoten (wenn Command darauf hört)
                    var pNode = vm.GetType().GetProperty("AusgewaehlterKnoten",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (pNode != null && pNode.CanWrite)
                    {
                        // Wenn Typ nicht passt, ignoriere (Commands nutzen Parameter – siehe XAML)
                        if (pNode.PropertyType.IsInstanceOfType(row))
                            pNode.SetValue(vm, row);
                    }
                }
            }

            // Requery, damit Buttons sofort (de)aktivieren
            CommandManager.InvalidateRequerySuggested();
        }

        // -------- TreeView: bestehende Logik beibehalten --------
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
