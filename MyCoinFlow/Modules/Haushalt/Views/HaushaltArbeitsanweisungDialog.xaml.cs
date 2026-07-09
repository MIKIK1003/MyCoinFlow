using MyCoinFlow.Models;
using System.Collections.Generic;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class HaushaltArbeitsanweisungDialog
    {
        public HaushaltArbeitsanweisung Ergebnis { get; private set; }

        public HaushaltArbeitsanweisungDialog(HaushaltArbeitsanweisung? arbeitsanweisung = null)
        {
            InitializeComponent();

            Ergebnis = arbeitsanweisung == null
                ? new HaushaltArbeitsanweisung
                {
                    IconKey = "ClipboardTextOutline",
                    IstAktiv = true
                }
                : new HaushaltArbeitsanweisung
                {
                    Id = arbeitsanweisung.Id,
                    Bezeichnung = arbeitsanweisung.Bezeichnung,
                    Beschreibung = arbeitsanweisung.Beschreibung,
                    IconKey = arbeitsanweisung.IconKey,
                    IstAktiv = arbeitsanweisung.IstAktiv,
                    ErstelltAm = arbeitsanweisung.ErstelltAm,
                    GeaendertAm = arbeitsanweisung.GeaendertAm
                };

            IconBox.ItemsSource = BuildIcons();

            BezeichnungBox.Text = Ergebnis.Bezeichnung;
            BeschreibungBox.Text = Ergebnis.Beschreibung;
            IconBox.SelectedValue = string.IsNullOrWhiteSpace(Ergebnis.IconKey)
                ? "ClipboardTextOutline"
                : Ergebnis.IconKey;
        }

        private static List<AuswahlItem> BuildIcons()
        {
            return new List<AuswahlItem>
            {
                new("ClipboardTextOutline", "Arbeitsanweisung"),
                new("Broom", "Reinigen"),
                new("Water", "Wasser / Entkalken"),
                new("Vacuum", "Saugen"),
                new("WrenchOutline", "Warten"),
                new("EyeOutline", "Kontrollieren"),
                new("Tools", "Werkzeug"),
                new("CogOutline", "Technik"),
                new("CalendarCheckOutline", "Termin")
            };
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            var bezeichnung = BezeichnungBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(bezeichnung))
            {
                MessageBox.Show(this, "Bitte eine Bezeichnung erfassen.", "Pflichtfeld",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                BezeichnungBox.Focus();
                return;
            }

            Ergebnis.Bezeichnung = bezeichnung;
            Ergebnis.Beschreibung = BeschreibungBox.Text?.Trim() ?? "";
            Ergebnis.IconKey = IconBox.SelectedValue?.ToString() ?? "ClipboardTextOutline";
            Ergebnis.IstAktiv = true;

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private sealed class AuswahlItem
        {
            public string Key { get; }
            public string Text { get; }

            public AuswahlItem(string key, string text)
            {
                Key = key;
                Text = text;
            }
        }
    }
}