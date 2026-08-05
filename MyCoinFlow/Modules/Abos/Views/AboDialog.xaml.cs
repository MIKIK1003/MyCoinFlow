using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class AboDialog
    {
        private sealed record AuswahlItem(string Code, string Anzeige);
        private sealed record KontoItem(int Id, string Anzeige);

        public Abo? Ergebnis { get; private set; }

        public AboDialog(Abo? vorlage = null)
        {
            InitializeComponent();

            Title = vorlage == null ? "Neues Abo" : "Abo bearbeiten";

            // Kopie bearbeiten, damit Abbrechen nichts am Original ändert
            Ergebnis = vorlage == null
                ? new Abo()
                : new Abo
                {
                    Id = vorlage.Id,
                    Name = vorlage.Name,
                    AdresseId = vorlage.AdresseId,
                    AdresseName = vorlage.AdresseName,
                    Periodizitaet = vorlage.Periodizitaet,
                    ErwarteterBetrag = vorlage.ErwarteterBetrag,
                    BetragToleranzProzent = vorlage.BetragToleranzProzent,
                    Status = vorlage.Status,
                    GekuendigtAm = vorlage.GekuendigtAm,
                    KuendigungsfristTage = vorlage.KuendigungsfristTage,
                    KuendigenZum = vorlage.KuendigenZum,
                    VorwarnTage = vorlage.VorwarnTage,
                    ErwartetesKontoId = vorlage.ErwartetesKontoId,
                    WebseiteUrl = vorlage.WebseiteUrl,
                    Notiz = vorlage.Notiz
                };

            try
            {
                var db = new DatabaseService();

                AdresseBox.ItemsSource = db.LadeAdressen();

                KontoBox.ItemsSource = db.LadeKontenplan()
                    .Select(k =>
                    {
                        string unter = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                        return new KontoItem(k.Id, $"{k.Kontonummer:D4}{unter}  {k.Detail}");
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stammdaten konnten nicht geladen werden:\n" + ex.Message,
                    "Abo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            PeriodeBox.ItemsSource = new List<AuswahlItem>
            {
                new(AboPerioden.Monatlich, "Monatlich"),
                new(AboPerioden.Quartalsweise, "Quartalsweise"),
                new(AboPerioden.Halbjaehrlich, "Halbjährlich"),
                new(AboPerioden.Jaehrlich, "Jährlich")
            };

            StatusBox.ItemsSource = new List<AuswahlItem>
            {
                new(AboStatus.Aktiv, "Aktiv"),
                new(AboStatus.Gekuendigt, "Gekündigt"),
                new(AboStatus.Beendet, "Beendet")
            };

            // Werte in die Felder
            NameBox.Text = Ergebnis.Name;
            AdresseBox.SelectedValue = Ergebnis.AdresseId;
            PeriodeBox.SelectedValue = Ergebnis.Periodizitaet;
            BetragBox.Text = Ergebnis.ErwarteterBetrag?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
            ToleranzBox.Text = Ergebnis.BetragToleranzProzent.ToString("0.##", CultureInfo.CurrentCulture);
            StatusBox.SelectedValue = Ergebnis.Status;
            GekuendigtAmPicker.SelectedDate = Ergebnis.GekuendigtAm;
            KuendigenZumPicker.SelectedDate = Ergebnis.KuendigenZum;
            FristBox.Text = Ergebnis.KuendigungsfristTage?.ToString() ?? "";
            UpdateKuendigungsHinweis();
            VorwarnBox.Text = Ergebnis.VorwarnTage.ToString();
            KontoBox.SelectedValue = Ergebnis.ErwartetesKontoId;
            WebseiteBox.Text = Ergebnis.WebseiteUrl;
            NotizBox.Text = Ergebnis.Notiz;
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (Ergebnis == null) return;

            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Bitte einen Namen für das Abo erfassen.",
                    "Abo", MessageBoxButton.OK, MessageBoxImage.Information);
                NameBox.Focus();
                return;
            }

            Ergebnis.Name = name;
            Ergebnis.AdresseId = AdresseBox.SelectedValue as int?;
            Ergebnis.Periodizitaet = PeriodeBox.SelectedValue as string ?? AboPerioden.Monatlich;
            Ergebnis.Status = StatusBox.SelectedValue as string ?? AboStatus.Aktiv;
            Ergebnis.GekuendigtAm = GekuendigtAmPicker.SelectedDate;
            Ergebnis.ErwartetesKontoId = KontoBox.SelectedValue as int?;
            Ergebnis.WebseiteUrl = string.IsNullOrWhiteSpace(WebseiteBox.Text) ? null : WebseiteBox.Text.Trim();
            Ergebnis.Notiz = string.IsNullOrWhiteSpace(NotizBox.Text) ? null : NotizBox.Text.Trim();

            Ergebnis.ErwarteterBetrag = ParseDecimalOderNull(BetragBox.Text);

            var toleranz = ParseDecimalOderNull(ToleranzBox.Text);
            Ergebnis.BetragToleranzProzent = toleranz is >= 0 and <= 100 ? toleranz.Value : 10m;

            Ergebnis.KuendigungsfristTage = ParseIntOderNull(FristBox.Text);
            Ergebnis.KuendigenZum = KuendigenZumPicker.SelectedDate;

            var vorwarn = ParseIntOderNull(VorwarnBox.Text);
            Ergebnis.VorwarnTage = vorwarn is > 0 and <= 365 ? vorwarn.Value : 7;

            if (Ergebnis.Status == AboStatus.Gekuendigt && !Ergebnis.GekuendigtAm.HasValue)
                Ergebnis.GekuendigtAm = DateTime.Today;

            DialogResult = true;
        }

        // Ursprüngliche Textfarbe des Hinweises (wird beim ersten Update gemerkt,
        // damit wir nach einer Orange-Warnung sauber zurückwechseln können)
        private System.Windows.Media.Brush? _kuendigungsHinweisStandardBrush;

        private void KuendigungsDatum_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
            => UpdateKuendigungsHinweis();

        private void KuendigungsFrist_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => UpdateKuendigungsHinweis();

        // Rechnet aus gewünschtem Ende und Kündigungsfrist den spätesten
        // Kündigungstermin aus und zeigt ihn live in der Maske an.
        private void UpdateKuendigungsHinweis()
        {
            // Events können während InitializeComponent feuern, bevor alle Controls existieren
            if (KuendigungsHinweisText == null || KuendigenZumPicker == null || FristBox == null)
                return;

            var ende = KuendigenZumPicker.SelectedDate;

            if (!ende.HasValue)
            {
                KuendigungsHinweisText.Text = "";
                return;
            }

            var frist = ParseIntOderNull(FristBox.Text) ?? 0;
            var termin = ende.Value.Date.AddDays(-frist);
            var restTage = (termin - DateTime.Today).Days;

            string text = $"➜ Spätestens kündigen bis: {termin:dd.MM.yyyy}";

            if (restTage < 0)
                text += $"  – Termin seit {-restTage} Tag(en) verstrichen!";
            else if (restTage == 0)
                text += "  – HEUTE!";
            else
                text += $"  (noch {restTage} Tage)";

            _kuendigungsHinweisStandardBrush ??= KuendigungsHinweisText.Foreground;

            KuendigungsHinweisText.Text = text;
            KuendigungsHinweisText.Foreground = restTage <= 14
                ? System.Windows.Media.Brushes.OrangeRed
                : _kuendigungsHinweisStandardBrush;
        }

        private static decimal? ParseDecimalOderNull(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var t = text.Trim().Replace("'", "");
            if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                return v;
            if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out v))
                return v;

            return null;
        }

        private static int? ParseIntOderNull(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return int.TryParse(text.Trim(), out var v) ? v : null;
        }
    }
}
