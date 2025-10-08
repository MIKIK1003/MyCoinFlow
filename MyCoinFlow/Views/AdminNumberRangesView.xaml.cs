using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AdminNumberRangesView : UserControl
    {
        private readonly DatabaseService _db = new();
        private readonly ObservableCollection<NumberRangeRule> _rules = new();

        // Dropdown-Optionen zentral
        private static readonly string[] _richtungOptions = { "Ausgabe", "Einnahme", "Neutral" };
        private static readonly string[] _bezeichnungOptions =
        {
            "Einnahmen (Budgetiert)",
            "Ausgaben (Budgetiert)",
            "Investitionen (Budgetiert)",
            "Amortisationen (Budgetiert)",
            "Durchlaufkonten (nicht budgetiert)"
        };

        public AdminNumberRangesView()
        {
            InitializeComponent();

            try
            {
                // Harte Migration + Prüfung (wirft bei Problem)
                _db.AssertNumberRangeRulesSchema();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nummernkreis-Tabelle konnte nicht migriert werden:\n" + ex.Message,
                                "Admin – Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Error);
                // Weiter machen: Das Grid lädt dann ggf. ohne Bezeichnungsspalte
            }

            SetComboItemsSources();   // Combo-Options setzen
            Reload();                 // Lädt Liste; intern ruft LadeNummernRegeln() nochmals Ensure auf
        }

        private void SetComboItemsSources()
        {
            // Richtung
            var richtungCol = RulesGrid.Columns
                .OfType<DataGridComboBoxColumn>()
                .FirstOrDefault(c => Convert.ToString(c.Header)?.Contains("Richtung") == true);
            if (richtungCol != null)
            {
                richtungCol.ItemsSource = _richtungOptions;
                richtungCol.SelectedItemBinding = new Binding("Richtung")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Mode = BindingMode.TwoWay
                };
            }

            // Bezeichnung
            var bezCol = RulesGrid.Columns
                .OfType<DataGridComboBoxColumn>()
                .FirstOrDefault(c => Convert.ToString(c.Header)?.Contains("Bezeichnung") == true);
            if (bezCol != null)
            {
                bezCol.ItemsSource = _bezeichnungOptions;
                bezCol.SelectedItemBinding = new Binding("Bezeichnung")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Mode = BindingMode.TwoWay
                };
            }
        }

        private void Reload()
        {
            _rules.Clear();
            foreach (var r in _db.LadeNummernRegeln())
            {
                // Defensive Normalisierung (Richtung)
                if (!_richtungOptions.Contains(r.Richtung, StringComparer.OrdinalIgnoreCase))
                    r.Richtung = "Ausgabe";

                // Bezeichnung sinnvoll vorbelegen (kein Vorzeichen-Zwang bei Neutral)
                if (string.IsNullOrWhiteSpace(r.Bezeichnung) || !_bezeichnungOptions.Contains(r.Bezeichnung))
                {
                    if (r.Richtung.Equals("Einnahme", StringComparison.OrdinalIgnoreCase))
                        r.Bezeichnung = "Einnahmen (Budgetiert)";
                    else if (r.Richtung.Equals("Ausgabe", StringComparison.OrdinalIgnoreCase))
                        r.Bezeichnung = "Ausgaben (Budgetiert)";
                    else // Neutral
                        r.Bezeichnung = "Durchlaufkonten (nicht budgetiert)";
                }

                _rules.Add(r);
            }
            RulesGrid.ItemsSource = _rules;
        }

        private void Neu_Click(object sender, RoutedEventArgs e)
        {
            // Default belassen (Ausgabe) – Nutzer kann auf Neutral umstellen.
            var neu = new NumberRangeRule
            {
                RangeStart = 0,
                RangeEnd = 0,
                Richtung = "Ausgabe",
                Bezeichnung = "Ausgaben (Budgetiert)",
                IstBudgetkonto = false
            };
            _rules.Add(neu);
            RulesGrid.SelectedItem = neu;
            RulesGrid.ScrollIntoView(neu);
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            RulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Validierung (mit Neutral)
            foreach (var r in _rules)
            {
                if (r.RangeStart < 0 || r.RangeEnd < 0)
                {
                    MessageBox.Show("Bereiche dürfen nicht negativ sein.", "Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (r.RangeStart > r.RangeEnd)
                {
                    MessageBox.Show("Von (Nr.) muss ≤ Bis (Nr.) sein.", "Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!_richtungOptions.Contains(r.Richtung, StringComparer.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Richtung muss „Einnahme“, „Neutral“ oder „Ausgabe“ sein.", "Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(r.Bezeichnung) || !_bezeichnungOptions.Contains(r.Bezeichnung))
                {
                    MessageBox.Show("Bitte eine gültige Bezeichnung wählen.", "Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Persistieren
            foreach (var r in _rules.ToList())
            {
                if (r.Id == 0) r.Id = _db.SpeichereNummernRegel(r);
                else _db.AktualisiereNummernRegel(r);
            }

            MessageBox.Show("Regeln gespeichert.", "Nummernkreise", MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
        }

        private void Loeschen_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not NumberRangeRule sel) return;
            if (sel.Id != 0) _db.LoescheNummernRegel(sel.Id);
            _rules.Remove(sel);
        }

        private void Reload_Click(object sender, RoutedEventArgs e) => Reload();
    }
}
