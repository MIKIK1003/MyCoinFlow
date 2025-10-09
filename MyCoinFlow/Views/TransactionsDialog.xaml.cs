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

        private void BudgetCheck_Changed(object sender, RoutedEventArgs e) => UpdateUiForType();



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
                // Datenquellen
                VonKontoBox.ItemsSource = _db.LadeKontoLookup();
                NachKontoBox.ItemsSource = _db.LadeKontoLookup();
                BankBox.ItemsSource = _db.LadeGeldinstitute();
                AdresseBox.ItemsSource = _db.LadeAdressen();

                // Vorbelegung
                if (_prefDatum.HasValue) DatumBox.SelectedDate = _prefDatum.Value;
                if (_prefBetrag.HasValue) BetragBox.Text = _prefBetrag.Value.ToString("N2", CultureInfo.CurrentCulture);
                if (!string.IsNullOrWhiteSpace(_prefNotiz)) NotizBox.Text = _prefNotiz;
                if (_prefVonKontoId.HasValue) VonKontoBox.SelectedValue = _prefVonKontoId.Value;
                if (_prefNachKontoId.HasValue) NachKontoBox.SelectedValue = _prefNachKontoId.Value;
                if (_prefGeldinstitutId.HasValue) BankBox.SelectedValue = _prefGeldinstitutId.Value;
                if (_prefAdresseId.HasValue) AdresseBox.SelectedValue = _prefAdresseId.Value;

                Title = _editId.HasValue ? "Buchung bearbeiten" : "Neue Buchung";

                // ---- Typ-Erkennung nur aus Feldern ----
                bool isBudgetLeg =
                    string.Equals((_prefNotiz ?? "").Trim(), "Budgetierte Einnahme", StringComparison.OrdinalIgnoreCase);

                bool nachIstEinnahmen =
                    _prefNachKontoId.HasValue && Safe(() => _db.IstEinnahmenKonto(_prefNachKontoId.Value));

                bool isAdresseBankEinnahme =
                    _prefGeldinstitutId.HasValue &&
                    !_prefVonKontoId.HasValue &&
                    _prefNachKontoId.HasValue &&
                    nachIstEinnahmen;

                bool isBankToKontoAusgabe =
                    _prefGeldinstitutId.HasValue &&
                    _prefNachKontoId.HasValue &&
                    !nachIstEinnahmen;

                bool isKontoZuKonto =
                    _prefVonKontoId.HasValue && _prefNachKontoId.HasValue;

                bool isKontoZuBank =
                    _prefVonKontoId.HasValue && _prefGeldinstitutId.HasValue && !_prefNachKontoId.HasValue;

                bool isAdresseBankRefund =
                    _prefGeldinstitutId.HasValue &&
                    _prefVonKontoId.HasValue &&
                    !_prefNachKontoId.HasValue &&
                    _prefAdresseId.HasValue;

                // Auswahl + Lock-Flag
                bool lockThisType = false;  // nur für Spezialfälle im Edit
                if (isBudgetLeg)
                {
                    TypeAdresseToBank.IsChecked = true;        // Optik ok
                    BudgetEinnahmeBox.IsChecked = true;
                    lockThisType = true;
                }
                else if (isAdresseBankEinnahme)
                {
                    TypeAdresseToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = true;
                    lockThisType = true;
                }
                else if (isBankToKontoAusgabe)
                {
                    TypeBankToKonto.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                    lockThisType = false;
                }
                else if (isKontoZuKonto)
                {
                    TypeKontoToKonto.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                    lockThisType = false;
                }
                else if (isKontoZuBank)
                {
                    // >>> WICHTIG: Konto→Bank VOR Adresse→Bank(Refund) behandeln!
                    TypeKontoToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                    lockThisType = false;
                }
                else if (isAdresseBankRefund)
                {
                    TypeAdresseToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                    lockThisType = true;
                }
                else
                {
                    // Fallback auf früher ermittelten Typnamen
                    switch (_prefTypName ?? "Bank → Konto")
                    {
                        case "Konto → Konto": TypeKontoToKonto.IsChecked = true; break;
                        case "Konto → Bank": TypeKontoToBank.IsChecked = true; break;
                        case "Adresse → Bank": TypeAdresseToBank.IsChecked = true; break;
                        default: TypeBankToKonto.IsChecked = true; break;
                    }
                    BudgetEinnahmeBox.IsChecked = false;
                    lockThisType = false;
                }

                // Sperren NUR für Spezialfälle im Edit
                bool isEdit = _editId.HasValue;
                bool lockTypeForThisEdit = isEdit && lockThisType;

                TypeBankToKonto.IsEnabled = !lockTypeForThisEdit;
                TypeKontoToKonto.IsEnabled = !lockTypeForThisEdit;
                TypeKontoToBank.IsEnabled = !lockTypeForThisEdit;
                TypeAdresseToBank.IsEnabled = !lockTypeForThisEdit;
                BudgetEinnahmeBox.IsEnabled = !lockTypeForThisEdit;

                UpdateUiForType();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dialog konnte nicht initialisiert werden:\n" + ex.Message,
                    "Transaktionen", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
        }


        private void TxnType_Checked(object sender, RoutedEventArgs e) => UpdateUiForType();

        private void UpdateUiForType()
        {
            // Reset
            SafeSetEnabled(VonKontoBox, false);
            SafeSetEnabled(NachKontoBox, false);
            SafeSetEnabled(BankBox, false);
            SafeSetEnabled(AdresseBox, true);

            // Bank → Konto
            if (TypeBankToKonto.IsChecked == true)
            {
                SafeSetEnabled(BankBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
            }

            // Konto → Konto
            if (TypeKontoToKonto.IsChecked == true)
            {
                SafeSetEnabled(VonKontoBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
            }

            // Konto → Bank
            if (TypeKontoToBank.IsChecked == true)
            {
                SafeSetEnabled(VonKontoBox, true);
                SafeSetEnabled(BankBox, true);
                return;
            }

            // Adresse → Bank
            if (TypeAdresseToBank.IsChecked == true)
            {
                // Für BEIDE Varianten (Einnahme+Refund) sollen Bank und Konto editierbar sein:
                // - Haken an  -> Konto = Einnahmenkonto (Nach-Konto)
                // - Haken aus -> Konto = Rückzahlungs-Konto (Von-Konto)
                SafeSetEnabled(BankBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
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
                var datum = DatumBox.SelectedDate ?? DateTime.Today;

                if (!decimal.TryParse(BetragBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var betrag) || betrag <= 0m)
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

                // Typ ausschließlich aus Radio-Buttons
                string typ;
                if (TypeKontoToKonto.IsChecked == true) typ = "Konto → Konto";
                else if (TypeKontoToBank.IsChecked == true) typ = "Konto → Bank";
                else if (TypeAdresseToBank.IsChecked == true) typ = "Adresse → Bank";
                else typ = "Bank → Konto";

                // Guard: "Bank → Konto" nicht auf Einnahmenkonto
                if (typ == "Bank → Konto" && nachKontoId.HasValue && _db.IstEinnahmenKonto(nachKontoId.Value))
                {
                    MessageBox.Show(
                        "Das gewählte Budgetkonto ist als 'Einnahmen' klassifiziert.\n" +
                        "Bei 'Bank → Konto' würde der Banksaldo steigen.",
                        "Prüfung Buchungstyp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool haken = (BudgetEinnahmeBox != null) && (BudgetEinnahmeBox.IsChecked == true);

                // ===== EDIT =====  (Nur diese eine Zeile anpassen – keine Zweitbuchung)
                if (_editId.HasValue)
                {
                    switch (typ)
                    {
                        case "Konto → Konto":
                            if (!vonKontoId.HasValue || !nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte Von- und Nach-Konto wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum, vonKontoId, nachKontoId, betrag, notiz, adresseId, null);
                            break;

                        case "Konto → Bank":
                            if (!vonKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Von-Konto und Geldinstitut wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum, vonKontoId, null, betrag, notiz, adresseId, bankId);
                            break;

                        case "Adresse → Bank":
                            {
                                if (!bankId.HasValue)
                                {
                                    MessageBox.Show("Bitte Geldinstitut wählen.", "Hinweis",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                    return;
                                }

                                if (haken)
                                {
                                    // Einnahme-Variante: EIN Satz – NachKontoId = Einnahmenkonto, Bank gesetzt
                                    if (!nachKontoId.HasValue)
                                    {
                                        MessageBox.Show("Bitte das Einnahmen-Konto (Nach-Konto) wählen.", "Hinweis",
                                            MessageBoxButton.OK, MessageBoxImage.Information);
                                        return;
                                    }
                                    var not = string.IsNullOrWhiteSpace(notiz) ? "Budgetierte Einnahme" : notiz;

                                    _db.AktualisiereTransaktion(
                                        _editId.Value, datum,
                                        vonKontoId: null,
                                        nachKontoId: nachKontoId,
                                        betrag: betrag,
                                        notiz: not,
                                        adresseId: adresseId,
                                        geldinstitutId: bankId
                                    );
                                }
                                else
                                {
                                    // Refund-Variante: EIN Satz – VonKontoId = Budgetkonto, Bank gesetzt
                                    if (!nachKontoId.HasValue)
                                    {
                                        MessageBox.Show("Bitte das Rückzahlungs-Konto wählen (Budget-Konto).", "Hinweis",
                                            MessageBoxButton.OK, MessageBoxImage.Information);
                                        return;
                                    }

                                    _db.AktualisiereTransaktion(
                                        _editId.Value, datum,
                                        vonKontoId: nachKontoId,   // Quelle = Budgetkonto
                                        nachKontoId: null,
                                        betrag: betrag,
                                        notiz: notiz,
                                        adresseId: adresseId,
                                        geldinstitutId: bankId
                                    );
                                }
                                break;
                            }

                        default: // "Bank → Konto"
                            if (!nachKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum, null, nachKontoId, betrag, notiz, adresseId, bankId);
                            break;
                    }

                    DialogResult = true;
                    return;
                }

                // ===== NEU =====  (einzelner Satz – identisch interpretierbar wie via Import)
                switch (typ)
                {
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

                        if (haken)
                        {
                            // Einnahme-Variante: EIN Satz – NachKontoId = Einnahmenkonto, Bank gesetzt
                            if (!nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte das Einnahmen-Konto (Nach-Konto) wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            var not = string.IsNullOrWhiteSpace(notiz) ? "Budgetierte Einnahme" : notiz;

                            _db.SpeichereTransaktion(datum, null, nachKontoId, betrag, not, adresseId, bankId);
                        }
                        else
                        {
                            // Refund-Variante: EIN Satz – VonKontoId = Budgetkonto, Bank gesetzt
                            if (!nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte das Rückzahlungs-Konto wählen (Budget-Konto).", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            _db.SpeichereTransaktion(datum, nachKontoId, null, betrag, notiz, adresseId, bankId);
                        }
                        break;

                    default: // "Bank → Konto"
                        if (!nachKontoId.HasValue || !bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, null, nachKontoId, betrag, notiz, adresseId, bankId);
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
