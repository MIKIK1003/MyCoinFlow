using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class TransactionsDialog : Window
    {
        private readonly DatabaseService _db = new();

        // Edit-Modus
        private int? _editId;

        // Vorbelegungen (vom Aufrufer gesetzt)
        private int? _prefVonKontoId;
        private int? _prefNachKontoId;
        private int? _prefGeldinstitutId;
        private int? _prefAdresseId;
        private DateTime? _prefDatum;
        private DateTime? _prefBudgetDatum;
        private decimal? _prefBetrag;
        private string? _prefNotiz;
        private string? _prefTypName;

        public TransactionsDialog()
        {
            InitializeComponent();
        }

        public TransactionsDialog(Transaktion? t) : this()
        {
            if (t == null) return;

            _editId = t.Id;
            _prefDatum = t.Datum;

            _prefBudgetDatum = t.BudgetDatum; // NEU: Budgetdatum für Edit-Vorbelegung merken

            _prefVonKontoId = t.VonKontoId;
            _prefNachKontoId = t.NachKontoId;
            _prefGeldinstitutId = t.GeldinstitutId;
            _prefAdresseId = t.AdresseId;
            _prefBetrag = t.Betrag;
            _prefNotiz = t.Notiz;
            _prefTypName = BestimmeTypName(t);
        }

        private string BestimmeTypName(Transaktion t)
        {
            if (string.Equals((t.Notiz ?? "").Trim(), "Budgetierte Einnahme", StringComparison.OrdinalIgnoreCase))
                return "Budgetierte Einnahme";

            if (!t.VonKontoId.HasValue && t.NachKontoId.HasValue) return "Bank → Konto";
            if (t.VonKontoId.HasValue && t.NachKontoId.HasValue) return "Konto → Konto";
            if (t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return "Konto → Bank";
            if (t.AdresseId.HasValue && t.GeldinstitutId.HasValue &&
                !t.VonKontoId.HasValue && !t.NachKontoId.HasValue) return "Adresse → Bank";

            return "Bank → Konto";
        }

        // ======= Loaded =======
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

                // NEU: Budgetdatum (Override) vorbefüllen
                if (_prefBudgetDatum.HasValue) BudgetDatumBox.SelectedDate = _prefBudgetDatum.Value;

                if (_prefBetrag.HasValue) BetragBox.Text = _prefBetrag.Value.ToString("N2", CultureInfo.CurrentCulture);
                if (!string.IsNullOrWhiteSpace(_prefNotiz)) NotizBox.Text = _prefNotiz;
                if (_prefVonKontoId.HasValue) VonKontoBox.SelectedValue = _prefVonKontoId.Value;
                if (_prefNachKontoId.HasValue) NachKontoBox.SelectedValue = _prefNachKontoId.Value;
                if (_prefGeldinstitutId.HasValue) BankBox.SelectedValue = _prefGeldinstitutId.Value;
                if (_prefAdresseId.HasValue) AdresseBox.SelectedValue = _prefAdresseId.Value;

                Title = _editId.HasValue ? "Buchung bearbeiten" : "Neue Buchung";

                // --- bestehende Typ-Erkennung (vereinfacht aus Feldern) ---
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

                if (isBudgetLeg)
                {
                    TypeAdresseToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = true;
                }
                else if (isAdresseBankEinnahme)
                {
                    TypeAdresseToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = true;
                }
                else if (isBankToKontoAusgabe)
                {
                    TypeBankToKonto.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                }
                else if (isKontoZuKonto)
                {
                    TypeKontoToKonto.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                }
                else if (isKontoZuBank)
                {
                    TypeKontoToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                }
                else if (isAdresseBankRefund)
                {
                    TypeAdresseToBank.IsChecked = true;
                    BudgetEinnahmeBox.IsChecked = false;
                }
                else
                {
                    switch (_prefTypName ?? "Bank → Konto")
                    {
                        case "Konto → Konto": TypeKontoToKonto.IsChecked = true; break;
                        case "Konto → Bank": TypeKontoToBank.IsChecked = true; break;
                        case "Adresse → Bank": TypeAdresseToBank.IsChecked = true; break;
                        default: TypeBankToKonto.IsChecked = true; break;
                    }
                    BudgetEinnahmeBox.IsChecked = false;
                }

                // Sperren nur für Spezialfälle im Edit
                bool isEdit = _editId.HasValue;
                bool lockThisType = isEdit && (isBudgetLeg || isAdresseBankEinnahme || isAdresseBankRefund);

                TypeBankToKonto.IsEnabled = !lockThisType;
                TypeKontoToKonto.IsEnabled = !lockThisType;
                TypeKontoToBank.IsEnabled = !lockThisType;
                TypeAdresseToBank.IsEnabled = !lockThisType;
                BudgetEinnahmeBox.IsEnabled = !lockThisType;

                UpdateUiForType();

                // >>> Hinweis Budgetzeitraum
                TryShowBudgetHint();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dialog konnte nicht initialisiert werden:\n" + ex.Message,
                    "Transaktionen", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
        }


        private void TxnType_Checked(object sender, RoutedEventArgs e) => UpdateUiForType();
        private void BudgetCheck_Changed(object sender, RoutedEventArgs e) => UpdateUiForType();

        private void UpdateUiForType()
        {
            SafeSetEnabled(VonKontoBox, false);
            SafeSetEnabled(NachKontoBox, false);
            SafeSetEnabled(BankBox, false);
            SafeSetEnabled(AdresseBox, true);

            if (TypeBankToKonto.IsChecked == true)
            {
                SafeSetEnabled(BankBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
            }
            if (TypeKontoToKonto.IsChecked == true)
            {
                SafeSetEnabled(VonKontoBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
            }
            if (TypeKontoToBank.IsChecked == true)
            {
                SafeSetEnabled(VonKontoBox, true);
                SafeSetEnabled(BankBox, true);
                return;
            }
            if (TypeAdresseToBank.IsChecked == true)
            {
                SafeSetEnabled(BankBox, true);
                SafeSetEnabled(NachKontoBox, true);
                return;
            }
        }

        private static void SafeSetEnabled(Control? c, bool enabled)
        {
            if (c != null) c.IsEnabled = enabled;
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Buchen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var datum = DatumBox.SelectedDate ?? DateTime.Today;

                // NEU: Budgetdatum (optional) einlesen
                DateTime? budgetDatum = BudgetDatumBox?.SelectedDate; // NEU
                if (budgetDatum.HasValue && budgetDatum.Value.Date == datum.Date)
                    budgetDatum = null; // NEU: gleiches Datum nicht als Override speichern

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

                string typ;
                if (TypeKontoToKonto.IsChecked == true) typ = "Konto → Konto";
                else if (TypeKontoToBank.IsChecked == true) typ = "Konto → Bank";
                else if (TypeAdresseToBank.IsChecked == true) typ = "Adresse → Bank";
                else typ = "Bank → Konto";

                // Guard: Bank → Konto darf nicht auf Einnahmenkonto
                if (typ == "Bank → Konto" && nachKontoId.HasValue && _db.IstEinnahmenKonto(nachKontoId.Value))
                {
                    MessageBox.Show(
                        "Das gewählte Budgetkonto ist als 'Einnahmen' klassifiziert.\n" +
                        "Bei 'Bank → Konto' würde der Banksaldo steigen.",
                        "Prüfung Buchungstyp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool haken = BudgetEinnahmeBox?.IsChecked == true;

                // EDIT: nur die eine Zeile ändern
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
                            _db.AktualisiereTransaktion(_editId.Value, datum, vonKontoId, nachKontoId, betrag, notiz, adresseId, null, budgetDatum); // NEU
                            break;

                        case "Konto → Bank":
                            if (!vonKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Von-Konto und Geldinstitut wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum, vonKontoId, null, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
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
                                    if (!nachKontoId.HasValue)
                                    {
                                        MessageBox.Show("Bitte das Einnahmen-Konto (Nach-Konto) wählen.", "Hinweis",
                                            MessageBoxButton.OK, MessageBoxImage.Information);
                                        return;
                                    }
                                    var not = string.IsNullOrWhiteSpace(notiz) ? "Budgetierte Einnahme" : notiz;

                                    _db.AktualisiereTransaktion(_editId.Value, datum,
                                        null, nachKontoId, betrag, not, adresseId, bankId, budgetDatum); // NEU
                                }
                                else
                                {
                                    if (!nachKontoId.HasValue)
                                    {
                                        MessageBox.Show("Bitte das Rückzahlungs-Konto wählen (Budget-Konto).", "Hinweis",
                                            MessageBoxButton.OK, MessageBoxImage.Information);
                                        return;
                                    }

                                    _db.AktualisiereTransaktion(_editId.Value, datum,
                                        nachKontoId, null, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
                                }
                                break;
                            }

                        default: // Bank → Konto
                            if (!nachKontoId.HasValue || !bankId.HasValue)
                            {
                                MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.AktualisiereTransaktion(_editId.Value, datum, null, nachKontoId, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
                            break;
                    }

                    DialogResult = true;
                    return;
                }

                // NEU: ein Satz pro Variante
                switch (typ)
                {
                    case "Konto → Konto":
                        if (!vonKontoId.HasValue || !nachKontoId.HasValue)
                        {
                            MessageBox.Show("Bitte Von- und Nach-Konto wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, vonKontoId, nachKontoId, betrag, notiz, adresseId, null, budgetDatum); // NEU
                        break;

                    case "Konto → Bank":
                        if (!vonKontoId.HasValue || !bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Von-Konto und Geldinstitut wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, vonKontoId, null, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
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
                            if (!nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte das Einnahmen-Konto (Nach-Konto) wählen.", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            var not = string.IsNullOrWhiteSpace(notiz) ? "Budgetierte Einnahme" : notiz;

                            _db.SpeichereTransaktion(datum, null, nachKontoId, betrag, not, adresseId, bankId, budgetDatum); // NEU
                        }
                        else
                        {
                            if (!nachKontoId.HasValue)
                            {
                                MessageBox.Show("Bitte das Rückzahlungs-Konto wählen (Budget-Konto).", "Hinweis",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            _db.SpeichereTransaktion(datum, nachKontoId, null, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
                        }
                        break;

                    default: // Bank → Konto
                        if (!nachKontoId.HasValue || !bankId.HasValue)
                        {
                            MessageBox.Show("Bitte Geldinstitut und Nach-Konto wählen.", "Hinweis",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        _db.SpeichereTransaktion(datum, null, nachKontoId, betrag, notiz, adresseId, bankId, budgetDatum); // NEU
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

        private void BudgetDatumClear_Click(object sender, RoutedEventArgs e)
        {
            // NEU: Budgetdatum (Override) entfernen
            BudgetDatumBox.SelectedDate = null;
        }



        private static int? GetSelectedIntOrNull(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }
               

        /// <summary>
        /// Blendet den gelben Hinweis ein, wenn das gewählte Datum außerhalb des aktiven Budgetzeitraums liegt.
        /// Rein informativ. Keine Mechanikänderung.
        /// </summary>
        private void TryShowBudgetHint()
        {
            try
            {
                // Hinweis-Controls vorhanden?
                if (TxnBudgetHint == null || TxnBudgetHintText == null) return;

                // Standard: ausblenden
                TxnBudgetHint.Visibility = Visibility.Collapsed;
                TxnBudgetHintText.Text = string.Empty;

                // Datum aus Picker
                DateTime dt = (DatumBox?.SelectedDate ?? DateTime.Today).Date;

                var period = GetActiveBudgetPeriod();
                if (period == null) return;

                DateTime start = period.Item1.Date;
                DateTime end = period.Item2.Date;

                if (dt < start || dt > end)
                {
                    TxnBudgetHint.Visibility = Visibility.Visible;
                    TxnBudgetHintText.Text =
                        $"Diese Buchung ({dt:dd.MM.yyyy}) ist außerhalb vom Budgetzeitraum";
                }
            }
            catch
            {
                // still – rein informativ
            }
        }
        private Tuple<DateTime, DateTime>? GetActiveBudgetPeriod()
        {
            try
            {
                var id = _db.HoleAktivenBudgetzeitraumId();
                if (!id.HasValue) return null;

                var bz = _db.HoleBudgetzeitraum(id.Value);
                if (bz == null) return null;

                return Tuple.Create(bz.Startdatum, bz.Enddatum);
            }
            catch
            {
                return null;
            }
        }


        /// <summary>
        /// Liest Start/Ende des aktiven Budgetzeitraums per Reflection aus dem DatabaseService.
        /// Unterstützt mehrere plausible Methodennamen/Propertynamen. Gibt null zurück, wenn nichts gefunden.
        /// </summary>



    }
}
