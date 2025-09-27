using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class NeuerEintragDialog : Window
    {
        // Damit klar ist, ob ein neuer oder ein bestehender Eintrag bearbeitet wird
        private readonly KontoplanEintrag? _bearbeiteEintrag;

        public NeuerEintragDialog(KontoplanEintrag? eintrag = null)
        {
            InitializeComponent();
            _bearbeiteEintrag = eintrag;
            LadeDropdownDaten();

            if (_bearbeiteEintrag != null)
            {
                // Felder vorausfüllen bei Bearbeiten
                KontonummerBox.Text = _bearbeiteEintrag.Kontonummer.ToString();
                ArtComboBox.SelectedItem = ArtComboBox.Items.Cast<KontenArt>().FirstOrDefault(a => a.Bezeichnung == _bearbeiteEintrag.Art);
                GruppeComboBox.SelectedItem = GruppeComboBox.Items.Cast<KontenGruppe>().FirstOrDefault(g => g.Bezeichnung == _bearbeiteEintrag.Gruppe);
                UntergruppeComboBox.SelectedItem = UntergruppeComboBox.Items.Cast<KontenUnterGruppe>().FirstOrDefault(u => u.Bezeichnung == _bearbeiteEintrag.Untergruppe);
                DetailBox.Text = _bearbeiteEintrag.Detail ?? "";
            }
        }

        public int Kontonummer => int.TryParse(KontonummerBox.Text, out var nummer) ? nummer : 0;
        public string Art => (ArtComboBox.SelectedItem as KontenArt)?.Bezeichnung ?? string.Empty;
        public string Gruppe => (GruppeComboBox.SelectedItem as KontenGruppe)?.Bezeichnung ?? string.Empty;
        public string Untergruppe => (UntergruppeComboBox.SelectedItem as KontenUnterGruppe)?.Bezeichnung ?? string.Empty;
        public string Detail => DetailBox.Text;

        private void LadeDropdownDaten()
        {
            var db = new DatabaseService();

            List<KontenArt> arten = db.LadeKontenArten();
            List<KontenGruppe> gruppen = db.LadeKontenGruppen();
            List<KontenUnterGruppe> untergruppen = db.LadeKontenUnterGruppen();

            ArtComboBox.ItemsSource = arten;
            GruppeComboBox.ItemsSource = gruppen;
            UntergruppeComboBox.ItemsSource = untergruppen;
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            int kontonummer = int.TryParse(KontonummerBox.Text, out var nr) ? nr : 0;
            var art = (ArtComboBox.SelectedItem as KontenArt)?.Bezeichnung ?? "";
            var gruppe = (GruppeComboBox.SelectedItem as KontenGruppe)?.Bezeichnung ?? "";
            var untergruppe = (UntergruppeComboBox.SelectedItem as KontenUnterGruppe)?.Bezeichnung ?? "";
            var detail = DetailBox.Text;

            var db = new DatabaseService();

            if (_bearbeiteEintrag == null)
            {
                // Neuer Eintrag
                db.NeuenKontoplanEintragSpeichern(kontonummer, art, gruppe, untergruppe, detail);
            }
            else
            {
                // Bestehenden Eintrag aktualisieren
                db.KontenplanEintragAktualisieren(_bearbeiteEintrag.Id, kontonummer, art, gruppe, untergruppe, detail);
            }

            this.DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
