using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class KontoAuswahlDialog : Window
    {
        // Gemeinsamer Speicher für beide Property-Namen
        private int? _selectedKontoId;

        /// <summary>Rückgabe: ausgewählte Konto-Id (deutsch).</summary>
        public int? AusgewaehltesKontoId
        {
            get => _selectedKontoId;
            private set => _selectedKontoId = value;
        }

        /// <summary>Rückgabe: ausgewählte Konto-Id (englischer Alias, oft im Projekt verwendet).</summary>
        public int? SelectedKontoId
        {
            get => _selectedKontoId;
            private set => _selectedKontoId = value;
        }

        public KontoAuswahlDialog()
        {
            InitializeComponent();
            // Entweder hier laden...
            // LadeKonten();
            // ...oder im Loaded-Event (empfohlen, damit XAML bereits initialisiert ist).
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Einheitlich über unsere eigene Liste laden (aus Kontenplan),
            // damit Anzeige und SelectedValuePath sicher passen.
            LadeKonten();

            if (KontoBox.Items.Count > 0 && KontoBox.SelectedIndex < 0)
                KontoBox.SelectedIndex = 0;
        }

        private sealed class KontoItem
        {
            public int Id { get; set; }
            public string Anzeige { get; set; } = "";
        }

        private void LadeKonten()
        {
            var db = new DatabaseService();
            var konten = db.LadeKontenplan();

            var items = new List<KontoItem>();
            foreach (var k in konten
                     .OrderBy(k => k.Art)
                     .ThenBy(k => k.Gruppe)
                     .ThenBy(k => k.Untergruppe)
                     .ThenBy(k => k.Kontonummer)
                     .ThenBy(k => k.Detail))
            {
                string u = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                items.Add(new KontoItem
                {
                    Id = k.Id,
                    Anzeige = $"{k.Kontonummer:D4}{u}  {k.Detail}"
                });
            }

            // WICHTIG: In XAML muss SelectedValuePath="Id" und DisplayMemberPath="Anzeige" gesetzt sein.
            KontoBox.ItemsSource = items;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (KontoBox.SelectedValue is int id)
            {
                SelectedKontoId = id;          // setzt auch AusgewaehltesKontoId
                DialogResult = true;
                Close();
                return;
            }

            MessageBox.Show("Bitte ein Konto wählen.");
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
