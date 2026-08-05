using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.UI.Base;

namespace MyCoinFlow.Views
{
    public partial class DmsDocumentDialog : BaseWindow
    {
        private readonly bool _istNeu;
        private readonly DatabaseService _db = new();

        public string? Kategorie { get; private set; }
        public string? Beschreibung { get; private set; }
        public int? AdresseId { get; private set; }
        public DateTime? DokumentDatum { get; private set; }
        public decimal? Betrag { get; private set; }
        public string? AusgewaehlteDateiPfad { get; private set; }
        public bool IstGarantieschein { get; private set; }
        public DateTime? GarantieAblaufDatum { get; private set; }

        public DmsDocumentDialog(DmsDocument? bestehend = null)
        {
            InitializeComponent();

            _istNeu = bestehend == null;
            Title = _istNeu ? "Dokument hochladen" : "Dokument bearbeiten";
            HeaderText.Text = Title;

            DateiPanel.Visibility = _istNeu ? Visibility.Visible : Visibility.Collapsed;
            DateinameBox.Visibility = _istNeu ? Visibility.Collapsed : Visibility.Visible;

            KategorieBox.ItemsSource = _db.GetDistinctKategorien();

            // Auch das Standard-Kontextmenü der Eingabezeile um "Kategorie löschen" erweitern
            KategorieBox.Loaded += KategorieBox_Loaded;

            // Adressliste mit Leereintrag, damit eine Zuordnung auch wieder
            // aufgehoben werden kann (Id 0 = keine Adresse).
            var adressen = new List<Adresse> { new Adresse { Id = 0, Name = "(keine Adresse)" } };
            adressen.AddRange(_db.LadeAdressen().OrderBy(a => a.Name));
            AdresseBox.ItemsSource = adressen;
            AdresseBox.SelectedValue = 0;

            if (bestehend != null)
            {
                DateinameBox.Text = bestehend.FileName;
                KategorieBox.Text = bestehend.Kategorie ?? "";
                AdresseBox.SelectedValue = bestehend.AdresseId ?? 0;
                BeschreibungBox.Text = bestehend.Beschreibung ?? "";
                DokumentDatumPicker.SelectedDate = bestehend.DokumentDatum;

                // Verknüpft: Transaktionsbetrag (verbindlich), sonst erkannter Betrag
                var betrag = bestehend.TransBetrag ?? bestehend.ErkannterBetrag;
                BetragBox.Text = betrag?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";

                // Betrag/Datum einer verknüpften Buchung stammen aus der Transaktion –
                // dort ändern, nicht hier (sonst laufen Beleg und Buchung auseinander).
                if (bestehend.EntityType != null)
                {
                    BetragBox.IsReadOnly = true;
                    BetragBox.Focusable = false;
                    VerknuepfungText.Text =
                        $"Verknüpft mit {bestehend.VerknuepftMitAnzeige}" +
                        (string.IsNullOrWhiteSpace(bestehend.TransAdresseName) ? "" : $" · {bestehend.TransAdresseName}") +
                        ". Betrag stammt aus der Buchung und wird dort gepflegt; " +
                        "die Verknüpfung selbst über «Transaktion zuweisen» ändern. " +
                        "Ohne eigene Adresse wird die Adresse der Buchung angezeigt.";
                }
                else
                {
                    VerknuepfungText.Text =
                        "Noch keiner Transaktion zugeordnet. Der Betrag stammt aus der Texterkennung " +
                        "und kann hier korrigiert werden (verbessert das automatische Matching).";
                }

                GarantieCheckBox.IsChecked = bestehend.IstGarantieschein;
                if (bestehend.GarantieAblaufDatum.HasValue)
                    GarantieAblaufPicker.SelectedDate = bestehend.GarantieAblaufDatum.Value;
            }
            else
            {
                DokumentDatumPicker.SelectedDate = DateTime.Today;
                VerknuepfungText.Text =
                    "Nach dem Hochladen kann das Dokument über «Transaktion zuweisen» mit einer Buchung verknüpft werden.";
            }

            GarantiePanel.Visibility = GarantieCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- Kategorien verwalten ----------------

        /// <summary>
        /// Ergänzt das Standard-Kontextmenü der Eingabezeile (Ausschneiden/Kopieren/…)
        /// um den Punkt "Kategorie löschen" für die gerade eingetragene Kategorie.
        /// </summary>
        private void KategorieBox_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                KategorieBox.ApplyTemplate();

                if (KategorieBox.Template?.FindName("PART_EditableTextBox", KategorieBox) is not TextBox editor)
                    return;

                var menu = new ContextMenu();
                menu.Items.Add(new MenuItem { Header = "Ausschneiden", Command = ApplicationCommands.Cut });
                menu.Items.Add(new MenuItem { Header = "Kopieren", Command = ApplicationCommands.Copy });
                menu.Items.Add(new MenuItem { Header = "Einfügen", Command = ApplicationCommands.Paste });
                menu.Items.Add(new MenuItem { Header = "Alle auswählen", Command = ApplicationCommands.SelectAll });
                menu.Items.Add(new Separator());

                var loeschen = new MenuItem { Header = "Kategorie löschen…" };
                loeschen.Click += (_, __) => KategorieEntfernen(KategorieBox.Text);
                menu.Items.Add(loeschen);

                // Nur anbieten, wenn im Feld tatsächlich eine Kategorie steht
                menu.Opened += (_, __) =>
                    loeschen.IsEnabled = !string.IsNullOrWhiteSpace(KategorieBox.Text);

                editor.ContextMenu = menu;
            }
            catch
            {
                // Komfortfunktion – darf den Dialog nie blockieren.
            }
        }

        /// <summary>Rechtsklick auf einen Eintrag in der Auswahlliste.</summary>
        private void KategorieLoeschen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
                KategorieEntfernen(mi.DataContext as string);
        }

        private void KategorieEntfernen(string? kategorie)
        {
            kategorie = (kategorie ?? "").Trim();

            if (string.IsNullOrWhiteSpace(kategorie))
            {
                MessageBox.Show(this, "Es ist keine Kategorie ausgewählt.", "Kategorie löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var anzahl = _db.ZaehleDokumenteMitKategorie(kategorie);

                var frage = anzahl == 0
                    ? $"Kategorie \"{kategorie}\" aus der Liste entfernen?"
                    : $"Die Kategorie \"{kategorie}\" wird von {anzahl} Dokument(en) verwendet.\n\n" +
                      "Wirklich entfernen? Die Dokumente selbst bleiben erhalten, haben danach " +
                      "aber keine Kategorie mehr.";

                var ask = MessageBox.Show(this, frage, "Kategorie löschen",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (ask != MessageBoxResult.Yes) return;

                _db.LoescheKategorie(kategorie);

                // Liste neu aufbauen; stand die gelöschte Kategorie im Feld, wird es geleert
                var aktuell = KategorieBox.Text;
                KategorieBox.IsDropDownOpen = false;
                KategorieBox.ItemsSource = _db.GetDistinctKategorien();
                KategorieBox.Text = string.Equals(aktuell, kategorie, StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : aktuell;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Kategorie konnte nicht gelöscht werden:\n" + ex.Message,
                    "Kategorie löschen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GarantieCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            GarantiePanel.Visibility = GarantieCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DateiWaehlen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Dokument auswählen",
                Filter = "Dokumente (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                AusgewaehlteDateiPfad = dlg.FileName;
                DateiPfadBox.Text = dlg.FileName;
            }
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (_istNeu && string.IsNullOrWhiteSpace(AusgewaehlteDateiPfad))
            {
                MessageBox.Show(this, "Bitte zuerst eine Datei auswählen.", "Datei fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Kategorie = string.IsNullOrWhiteSpace(KategorieBox.Text) ? null : KategorieBox.Text.Trim();
            Beschreibung = string.IsNullOrWhiteSpace(BeschreibungBox.Text) ? null : BeschreibungBox.Text.Trim();
            DokumentDatum = DokumentDatumPicker.SelectedDate;

            // Id 0 ist der Leereintrag "(keine Adresse)"
            AdresseId = AdresseBox.SelectedValue is int adrId && adrId > 0 ? adrId : null;

            var betragText = (BetragBox.Text ?? "").Trim().Replace("'", "");
            if (string.IsNullOrWhiteSpace(betragText))
            {
                Betrag = null;
            }
            else if (decimal.TryParse(betragText, NumberStyles.Number, CultureInfo.CurrentCulture, out var b)
                     || decimal.TryParse(betragText, NumberStyles.Number, CultureInfo.InvariantCulture, out b))
            {
                Betrag = b;
            }
            else
            {
                MessageBox.Show(this, "Der Betrag konnte nicht gelesen werden. Bitte z.B. 84.60 eingeben.",
                    "Betrag ungültig", MessageBoxButton.OK, MessageBoxImage.Information);
                BetragBox.Focus();
                return;
            }

            IstGarantieschein = GarantieCheckBox.IsChecked == true;
            GarantieAblaufDatum = IstGarantieschein ? GarantieAblaufPicker.SelectedDate : null;

            DialogResult = true;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
