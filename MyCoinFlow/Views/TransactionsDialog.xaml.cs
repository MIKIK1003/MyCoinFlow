using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class TransactionsDialog : Window
    {
        private readonly DatabaseService _db = new();

        // Edit-Modus: bestehende Id (null => Neu)
        private int? _editId;

        // Vorbelegungen (optional)
        private int? _prefVonKontoId;
        private int? _prefNachKontoId;
        private int? _prefGeldinstitutId;
        private int? _prefAdresseId;
        private DateTime? _prefDatum;
        private decimal? _prefBetrag;
        private string? _prefNotiz;
        private string? _prefTypName; // "Bank → Konto" | "Konto → Konto" | "Konto → Bank" | "Adresse → Bank"

        public TransactionsDialog()
        {
            InitializeComponent();
        }

        public TransactionsDialog(Transaktion? t) : this()
        {
            if (t != null)
            {
                _editId = t.Id; // Edit-Id merken
                _prefDatum = t.Datum;
                _prefVonKontoId = t.VonKontoId;
                _prefNachKontoId = t.NachKontoId;
                _prefGeldinstitutId = t.GeldinstitutId;
                _prefAdresseId = t.AdresseId;
                _prefBetrag = t.Betrag;
                _prefNotiz = t.Notiz;
                _prefTypName = BestimmeTypName(t);
            }
        }

        private static string BestimmeTypName(Transaktion t)
        {
            if (!t.VonKontoId.HasValue && t.NachKontoId.HasValue) return "Bank → Konto";
            if (t.VonKontoId.HasValue && t.NachKontoId.HasValue) return "Konto → Konto";
            if (t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return "Konto → Bank";
            if (t.AdresseId.HasValue && t.GeldinstitutId.HasValue &&
                !t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return "Adresse → Bank";
            return "Bank → Konto";
        }

        // ---------------- Loaded ----------------
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Datenquellen laden
                VonKontoBox.ItemsSource = _db.LadeKontoLookup();
                NachKontoBox.ItemsSource = _db.LadeKontoLookup();
                BankBox.ItemsSource = _db.LadeGeldinstitute();
                AdresseBox.ItemsSource = _db.LadeAdressen();

                // Vorbelegungen
                if (_prefDatum.HasValue) DatumBox.SelectedDate = _prefDatum.Value;
                if (_prefBetrag.HasValue) BetragBox.Text = _prefBetrag.Value.ToString("N2", CultureInfo.CurrentCulture);
                if (!string.IsNullOrWhiteSpace(_prefNotiz)) NotizBox.Text = _prefNotiz;
                if (_prefVonKontoId.HasValue) VonKontoBox.SelectedValue = _prefVonKontoId.Value;
                if (_prefNachKontoId.HasValue) NachKontoBox.SelectedValue = _prefNachKontoId.Value;
                if (_prefGeldinstitutId.HasValue) BankBox.SelectedValue = _prefGeldinstitutId.Value;
                if (_prefAdresseId.HasValue) AdresseBox.SelectedValue = _prefAdresseId.Value;

                // Titel je nach Modus
                this.Title = _editId.HasValue ? "Buchung bearbeiten" : "Neue Buchung";

                // Typ setzen (Default: Bank → Konto)
                switch (_prefTypName ?? "Bank → Konto")
                {
                    case "Konto → Konto": TypeKontoToKonto.IsChecked = true; break;
                    case "Konto → Bank": TypeKontoToBank.IsChecked = true; break;
                    case "Adresse → Bank": TypeAdresseToBank.IsChecked = true; break;
                    default: TypeBankToKonto.IsChecked = true; break;
                }

                UpdateUiForType();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dialog konnte nicht initialisiert werden:\n" + ex.Message,
                    "Transaktionen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxnType_Checked(object sender, RoutedEventArgs e) => UpdateUiForType();

        private void UpdateUiForType()
        {
            string typ = GetSelectedType();

            // Alles neutralisieren
            SafeSetEnabled(VonKontoBox, false);
            SafeSetEnabled(NachKontoBox, false);
            SafeSetEnabled(BankBox, false);
            SafeSetEnabled(AdresseBox, true); // Adresse generell erlaubt

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

        private string GetSelectedType()
        {
            if (TypeKontoToKonto.IsChecked == true) return "Konto → Konto";
            if (TypeKontoToBank.IsChecked == true) return "Konto → Bank";
            if (TypeAdresseToBank.IsChecked == true) return "Adresse → Bank";
            return "Bank → Konto";
        }

        private static void SafeSetEnabled(Control? c, bool enabled)
        {
            if (c != null) c.IsEnabled = enabled;
        }

        // ---------------- Buttons ----------------
        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Buchen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var typ = GetSelectedType();
                var datum = DatumBox.SelectedDate ?? DateTime.Today;

                if (!decimal.TryParse(BetragBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var betrag)
                    || betrag <= 0m)
                {
                    MessageBox.Show("Bitte einen Betrag > 0 eingeben.", "Hinweis",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int? vonKontoId = GetSelectedIntOrNull(VonKontoBox?.SelectedValue);
                int? nachKontoId = GetSelectedIntOrNull(NachKontoBox?.SelectedValue);
                int? bankId = GetSelectedIntOrNull(BankBox?.SelectedValue);
                int? adresseId = GetSelectedIntOrNull(AdresseBox?.SelectedValue);
                string? notiz = string.IsNullOrWhiteSpace(NotizBox.Text) ? null : NotizBox.Text.Trim();

                // --- Guard: Bei "Bank → Konto" MUSS das Nach-Konto ein Ausgaben-Konto sein ---
                if (typ == "Bank → Konto" && nachKontoId.HasValue)
                {
                    if (_db.IstEinnahmenKonto(nachKontoId.Value))
                    {
                        MessageBox.Show(
                            "Das gewählte Budgetkonto ist als 'Einnahmen' klassifiziert.\n" +
                            "Bei 'Bank → Konto' würde der Banksaldo steigen.\n\n" +
                            "Bitte ein 'Ausgaben'-Konto wählen.",
                            "Prüfung Buchungstyp",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // --- Edit: exakt eine Zeile aktualisieren ---
                if (_editId.HasValue)
                {
                    switch (typ)
                    {
                        case "Bank → Konto":
                            if (!nachKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum,
                                null, nachKontoId, betrag, notiz, adresseId, bankId);
                            break;

                        case "Konto → Konto":
                            if (!vonKontoId.HasValue || !nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte Von- und Nach-Konto wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum,
                                vonKontoId, nachKontoId, betrag, notiz, adresseId, null);
                            break;

                        case "Konto → Bank":
                            if (!vonKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Von-Konto und Geldinstitut wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum,
                                vonKontoId, null, betrag, notiz, adresseId, bankId);
                            break;

                        case "Adresse → Bank":
                            if (!bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Geldinstitut wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum,
                                null, null, betrag, notiz, adresseId, bankId);
                            break;
                    }

                    DialogResult = true;
                    return;
                }

                // --- Neu ---
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

                        _db.SpeichereTransaktion(datum, null, null, betrag, notiz, adresseId, bankId);

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

        private static int? GetSelectedIntOrNull(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            try { return Convert.ToInt32(value); }
            catch { return null; }
        }
    }
}
