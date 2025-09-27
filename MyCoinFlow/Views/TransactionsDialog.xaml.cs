using System;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class TransactionsDialog : Window
    {
        private readonly DatabaseService _db = new();

        // Vorbelegungen (optional)
        private int? _prefVonKontoId;
        private int? _prefNachKontoId;
        private int? _prefGeldinstitutId;
        private int? _prefAdresseId;
        private DateTime? _prefDatum;
        private decimal? _prefBetrag;
        private int? _prefTypIndex;

        // das ist ein Kontrolltext für Git

        public TransactionsDialog()
        {
            InitializeComponent();
            // Keine UI-Zugriffe hier – erst im Loaded!
        }

        public TransactionsDialog(Transaktion? t) : this()
        {
            if (t != null)
            {
                _prefDatum = t.Datum;
                _prefVonKontoId = t.VonKontoId;
                _prefNachKontoId = t.NachKontoId;
                _prefGeldinstitutId = t.GeldinstitutId;
                _prefAdresseId = t.AdresseId;
                _prefBetrag = t.Betrag;
                _prefTypIndex = BestimmeTypIndex(t);
            }
        }

        private int BestimmeTypIndex(Transaktion t)
        {
            // 0: Bank → Konto, 1: Konto → Konto, 2: Konto → Bank, 3: Adresse → Bank
            if (!t.VonKontoId.HasValue && t.NachKontoId.HasValue) return 0;
            if (t.VonKontoId.HasValue && t.NachKontoId.HasValue) return 1;
            if (t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return 2;
            if (t.AdresseId.HasValue && t.GeldinstitutId.HasValue &&
                !t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return 3;
            return 0;
        }

        // ---------- Loaded ----------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Datenquellen
                VonKontoBox.ItemsSource = _db.LadeKontoLookup();
                NachKontoBox.ItemsSource = _db.LadeKontoLookup();
                BankBox.ItemsSource = _db.LadeGeldinstitute();
                AdresseBox.ItemsSource = _db.LadeAdressen();

                // Vorbelegung
                if (_prefTypIndex.HasValue) TypBox.SelectedIndex = _prefTypIndex.Value;
                if (_prefDatum.HasValue) DatumBox.SelectedDate = _prefDatum.Value;
                if (_prefBetrag.HasValue) BetragBox.Text = _prefBetrag.Value.ToString("N2");

                if (_prefVonKontoId.HasValue) VonKontoBox.SelectedValue = _prefVonKontoId.Value;
                if (_prefNachKontoId.HasValue) NachKontoBox.SelectedValue = _prefNachKontoId.Value;
                if (_prefGeldinstitutId.HasValue) BankBox.SelectedValue = _prefGeldinstitutId.Value;
                if (_prefAdresseId.HasValue) AdresseBox.SelectedValue = _prefAdresseId.Value;

                UpdateUiForType();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dialog konnte nicht initialisiert werden:\n" + ex.Message,
                    "Transaktionen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TypBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateUiForType();

        private void UpdateUiForType()
        {
            if (TypBox == null) return;

            string typ = (TypBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bank → Konto";

            // Erst alles neutralisieren
            SafeSetEnabled(VonKontoBox, false);
            SafeSetEnabled(NachKontoBox, false);
            SafeSetEnabled(BankBox, false);

            // Adresse: immer erlaubt (wie gewünscht, auch bei Bank→Konto)
            SafeSetEnabled(AdresseBox, true);

            switch (typ)
            {
                case "Bank → Konto":
                    SafeSetEnabled(BankBox, true);
                    SafeSetEnabled(NachKontoBox, true);
                    break;

                case "Konto → Konto":
                    SafeSetEnabled(VonKontoBox, true);
                    SafeSetEnabled(NachKontoBox, true);
                    break;

                case "Konto → Bank":
                    SafeSetEnabled(VonKontoBox, true);
                    SafeSetEnabled(BankBox, true);
                    break;

                case "Adresse → Bank":
                    SafeSetEnabled(BankBox, true);
                    break;
            }
        }

        private static void SafeSetEnabled(Control? c, bool enabled)
        {
            if (c != null) c.IsEnabled = enabled;
        }

        // ---------- Buttons ----------
        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Buchen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var typ = (TypBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bank → Konto";
                var datum = DatumBox.SelectedDate ?? DateTime.Today;

                if (!decimal.TryParse(BetragBox.Text, out var betrag) || betrag <= 0m)
                {
                    MessageBox.Show("Bitte einen Betrag > 0 eingeben.", "Hinweis",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int? vonKontoId = VonKontoBox.SelectedValue as int?;
                int? nachKontoId = NachKontoBox.SelectedValue as int?;
                int? bankId = BankBox.SelectedValue as int?;
                int? adresseId = AdresseBox.SelectedValue as int?;
                string? notiz = string.IsNullOrWhiteSpace(NotizBox.Text) ? null : NotizBox.Text.Trim();

                switch (typ)
                {
                    case "Bank → Konto":
                        if (!nachKontoId.HasValue || !bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, null, nachKontoId, betrag, notiz, adresseId, bankId);
                        break;

                    case "Konto → Konto":
                        if (!vonKontoId.HasValue || !nachKontoId.HasValue)
                        {
                            MessageBox.Show("Bitte Von- und Nach-Konto wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, vonKontoId, nachKontoId, betrag, notiz, adresseId, null);
                        break;

                    case "Konto → Bank":
                        if (!vonKontoId.HasValue || !bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Von-Konto und Geldinstitut wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, vonKontoId, null, betrag, notiz, adresseId, bankId);
                        break;

                    case "Adresse → Bank":
                        if (!bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Geldinstitut wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        // Bankbuchung immer
                        _db.SpeichereTransaktion(datum, null, null, betrag, notiz, adresseId, bankId);

                        // Zusatz: falls Adresse budgetiert
                        if (adresseId.HasValue)
                        {
                            var adr = _db.LadeAdresseById(adresseId.Value);
                            if (adr.IstBudgetiert && adr.StandardEinnahmenKontoId.HasValue)
                            {
                                _db.SpeichereTransaktion(datum, null, adr.StandardEinnahmenKontoId, betrag,
                                                         "Budgetierte Einnahme", adresseId, null);
                            }
                        }
                        break;

                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Buchung fehlgeschlagen:\n" + ex.Message,
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
