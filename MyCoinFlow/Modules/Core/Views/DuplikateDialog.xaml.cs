using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Findet doppelte Transaktionen (gleiches Datum + gleicher Betrag), zeigt sie
    /// gruppiert an und löscht die vom Benutzer markierten Einträge.
    /// </summary>
    public partial class DuplikateDialog
    {
        public sealed class DupRow : INotifyPropertyChanged
        {
            private bool _loeschen;
            public bool Loeschen
            {
                get => _loeschen;
                set { if (_loeschen == value) return; _loeschen = value; OnPropertyChanged(); }
            }

            public int GruppeNr { get; set; }
            public bool GruppeGerade => GruppeNr % 2 == 0;

            public int Id { get; set; }
            public DateTime Datum { get; set; }
            public decimal Betrag { get; set; }
            public string? AdresseName { get; set; }
            public string? BankName { get; set; }
            public string? Notiz { get; set; }
            public int AnhangAnzahl { get; set; }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly DatabaseService _db = new();
        private List<DupRow> _rows = new();

        /// <summary>True, sobald mindestens eine Transaktion gelöscht wurde (Aufrufer lädt dann neu).</summary>
        public bool HatGeloescht { get; private set; }

        public DuplikateDialog()
        {
            InitializeComponent();
            SucheDuplikate();
        }

        private void SucheDuplikate()
        {
            try
            {
                var alle = _db.SucheTransaktionen(null, null, null, null);
                bool nurIdentischeNotiz = NurIdentischeNotizCheck.IsChecked == true;

                var gruppen = alle
                    .GroupBy(t => nurIdentischeNotiz
                        ? (object)(t.Datum.Date, t.Betrag, (t.Notiz ?? "").Trim())
                        : (object)(t.Datum.Date, t.Betrag))
                    .Where(g => g.Count() >= 2)
                    .OrderByDescending(g => g.First().Datum)
                    .ToList();

                _rows = new List<DupRow>();
                int nr = 0;

                foreach (var g in gruppen)
                {
                    nr++;
                    foreach (var t in g.OrderBy(t => t.Id))
                    {
                        int anhaenge = 0;
                        try { anhaenge = _db.LoadAttachmentDetailsByTransaktionId(t.Id).Count; } catch { }

                        _rows.Add(new DupRow
                        {
                            GruppeNr = nr,
                            Id = t.Id,
                            Datum = t.Datum,
                            Betrag = t.Betrag,
                            AdresseName = t.AdresseName,
                            BankName = t.BankName,
                            Notiz = t.Notiz,
                            AnhangAnzahl = anhaenge
                        });
                    }
                }

                DupGrid.ItemsSource = _rows;
                StatusText.Text = nr == 0
                    ? "Keine doppelten Transaktionen gefunden."
                    : $"{nr} Gruppe(n) mit insgesamt {_rows.Count} Transaktionen gefunden.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Duplikat-Suche fehlgeschlagen:\n" + ex.Message,
                    "Doppelte Transaktionen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            // Beim Umschalten des Filters neu suchen (Markierungen werden verworfen)
            if (IsLoaded) SucheDuplikate();
        }

        private void NeuereVormerken_Click(object sender, RoutedEventArgs e)
        {
            // Pro Gruppe bleibt der Eintrag mit der KLEINSTEN Id stehen (das Original),
            // alle später erfassten/importierten Duplikate werden markiert.
            foreach (var gruppe in _rows.GroupBy(r => r.GruppeNr))
            {
                var minId = gruppe.Min(r => r.Id);
                foreach (var row in gruppe)
                    row.Loeschen = row.Id != minId;
            }
        }

        private void AuswahlAufheben_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.Loeschen = false;
        }

        private void MarkierteLoeschen_Click(object sender, RoutedEventArgs e)
        {
            DupGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

            var markiert = _rows.Where(r => r.Loeschen).ToList();

            if (markiert.Count == 0)
            {
                MessageBox.Show("Keine Transaktionen markiert.",
                    "Doppelte Transaktionen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Schutz: In keiner Gruppe sollen ALLE Einträge verschwinden
            var kompletteGruppen = _rows
                .GroupBy(r => r.GruppeNr)
                .Where(g => g.All(r => r.Loeschen))
                .Select(g => g.Key)
                .ToList();

            if (kompletteGruppen.Count > 0)
            {
                var warn = MessageBox.Show(
                    $"In {kompletteGruppen.Count} Gruppe(n) sind ALLE Einträge markiert – " +
                    "damit bleibt dort kein Original stehen.\n\nTrotzdem fortfahren?",
                    "Doppelte Transaktionen", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (warn != MessageBoxResult.Yes) return;
            }

            var ask = MessageBox.Show(
                $"{markiert.Count} markierte Transaktion(en) wirklich löschen?\n\n" +
                "Abo-Zuordnungen werden automatisch mitentfernt; Transaktionen mit " +
                "anderen Verknüpfungen (z.B. STWE) werden übersprungen und gemeldet.",
                "Doppelte Transaktionen", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            int geloescht = 0;
            var fehler = new List<string>();

            foreach (var row in markiert)
            {
                try
                {
                    _db.LoescheTransaktion(row.Id);
                    geloescht++;
                }
                catch (Exception ex)
                {
                    fehler.Add($"#{row.Id} ({row.Datum:dd.MM.yyyy}, {row.Betrag:N2}): {ErsteZeile(ex.Message)}");
                }
            }

            if (geloescht > 0) HatGeloescht = true;

            var meldung = $"{geloescht} Transaktion(en) gelöscht.";
            if (fehler.Count > 0)
                meldung += $"\n\nNicht gelöscht ({fehler.Count}):\n" + string.Join("\n", fehler.Take(10));

            MessageBox.Show(meldung, "Doppelte Transaktionen",
                MessageBoxButton.OK,
                fehler.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            SucheDuplikate();
        }

        private static string ErsteZeile(string text)
        {
            var idx = text.IndexOfAny(new[] { '\r', '\n' });
            return idx > 0 ? text[..idx] : text;
        }
    }
}
