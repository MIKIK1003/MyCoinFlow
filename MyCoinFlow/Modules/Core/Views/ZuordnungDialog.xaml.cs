using System;
using System.Collections.Generic;
using System.Linq;
using MyCoinFlow.UI.Base;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class ZuordnungDialog : BaseWindow
    {
        private readonly DatabaseService _db = new();
        private readonly BankImportItem _item;

        // Rückgaben an den Aufrufer
        public int? SelectedAdresseId { get; private set; }
        public int? SelectedKontoId { get; private set; }

        public ZuordnungDialog(BankImportItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            InitializeComponent();
            // UI initial aufbauen
            InitUi();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Hinweis auf Budgetzeitraum (rein informativ)
            TryShowBudgetHint();
        }

        // ------------ UI Init ------------
        private void InitUi()
        {
            // 1) Kopf-Infos
            DataContext = new
            {
                BuchungInfo = $"{_item.BookingDate:yyyy-MM-dd}  |  {_item.Amount:N2} {_item.Currency}  |  {(_item.Direction == KreditDebit.Debit ? "Ausgabe" : "Einnahme")}",
                BuchungText = string.IsNullOrWhiteSpace(_item.Text) ? "(kein Buchungstext)" : _item.Text
            };

            // 2) DropDowns füllen
            AdrBox.ItemsSource = _db.LadeAdressen().OrderBy(a => a.Name).ToList();   // {Id, Name, ...}
            KontoBox.ItemsSource = _db.LadeKontoLookup();                               // {Id, Anzeige}

            // 3) Geldinstitut-Info
            GiInfoText.Text = !string.IsNullOrWhiteSpace(_item.AccountIban)
                ? $"Konto-IBAN: {_item.AccountIban}"
                : "unbekannt";

            // 4) Vorbelegung: per Vorschlag/IBAN/Name
            PreselectAdresseUndKonto();

            // 5) Erste Regel-Preview zeigen
            UpdateRulePreview();
        }

        private void UiChanged(object sender, RoutedEventArgs e)
        {
            UpdateRulePreview();
        }

        // ------------ Regel-Preview (nur Anzeige) ------------
        private void UpdateRulePreview()
        {
            // Adresse bestimmen (bestehend)
            Adresse adr = null;
            if (NeueAdresseCheck.IsChecked != true && AdrBox.SelectedValue is int adrId)
            {
                try { adr = _db.LadeAdresseById(adrId); } catch { adr = null; }
            }

            bool istUmbuchung = IstUmbuchungsAdresseName(adr != null ? adr.Name : null);
            bool istEinnahme = _item.Direction == KreditDebit.Credit;
            bool budgetFlag = BudgetEinnahmenCheck != null && BudgetEinnahmenCheck.IsChecked == true;

            // Kontotext robust ermitteln
            string kontoLabel = "(Konto auswählen)";
            if (KontoBox != null && KontoBox.SelectedItem != null)
            {
                var pi = KontoBox.SelectedItem.GetType().GetProperty("Anzeige");
                kontoLabel = Convert.ToString(pi != null ? pi.GetValue(KontoBox.SelectedItem, null) : null) ?? kontoLabel;
            }

            // Ableitung analog deiner Regeln:
            PackIconKind icon;
            string typText;
            string hint;

            if (!istEinnahme) // Debit
            {
                if (istUmbuchung)
                {
                    icon = PackIconKind.SwapHorizontal;
                    typText = "Umbuchung (Bank ↔ Bank)";
                    hint = "Durchlaufkonto (DefaultKonto der Umbuchungs-Adresse) wird verwendet.";
                }
                else
                {
                    icon = PackIconKind.BankTransfer;
                    typText = "Bank → Konto (Ausgabe)";
                    hint = $"Ziel (Nach-Konto): {kontoLabel}";
                }
            }
            else // Credit
            {
                if (budgetFlag)
                {
                    icon = PackIconKind.AccountCash;
                    typText = "Adresse → Bank (Einnahme)";
                    hint = $"Nach-Konto (Einnahmenkonto): {kontoLabel}";
                }
                else
                {
                    icon = PackIconKind.Bank;
                    typText = "Konto → Bank (Refund)";
                    hint = $"Von-Konto (Rückzahlungskonto): {kontoLabel}";
                }
            }

            TypIcon.Kind = icon;
            TypText.Text = typText;
            TypHint.Text = hint;
        }

        private static bool IstUmbuchungsAdresseName(string name)
            => !string.IsNullOrWhiteSpace(name) &&
               name.Trim().StartsWith("Interne Umbuchung", StringComparison.CurrentCultureIgnoreCase);

        // ------------ Buttons ------------
        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int? kontoId = GetSelectedIntOrNull(KontoBox != null ? KontoBox.SelectedValue : null);
                if (kontoId == null)
                {
                    MessageBox.Show("Bitte ein Standardkonto wählen.", "Anlernen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool istEinnahme = _item.Direction == KreditDebit.Credit;
                var budgetCheck = BudgetEinnahmenCheck;

                // Adresse bestimmen oder neu anlegen
                int? adrId = null;
                bool regelSpeichern = false;
                int? regelKontoId = null;

                if (NeueAdresseCheck != null && NeueAdresseCheck.IsChecked == true)
                {
                    var name = (NeuNameBox.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Bitte einen Namen für die neue Adresse eingeben.", "Anlernen",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        NeuNameBox.Focus();
                        return;
                    }

                    var ibanRaw = NeuIbanBox.Text != null ? NeuIbanBox.Text.Trim() : null;
                    string iban = string.IsNullOrWhiteSpace(ibanRaw) ? null : ibanRaw.Replace(" ", "").ToUpperInvariant();

                    var adrNeu = new Adresse { Name = name, IBAN = iban };

                    // Neue Adresse:
                    // Das gewählte Konto wird Standard der Adresse.
                    if (istEinnahme && budgetCheck != null && budgetCheck.IsChecked == true)
                    {
                        adrNeu.IstBudgetiert = true;
                        adrNeu.StandardEinnahmenKontoId = kontoId;
                        adrNeu.DefaultKontoId = null;
                    }
                    else
                    {
                        adrNeu.IstBudgetiert = false;
                        adrNeu.StandardEinnahmenKontoId = null;
                        adrNeu.DefaultKontoId = kontoId;
                    }

                    adrId = _db.SpeichereAdresse(adrNeu);
                }
                else
                {
                    adrId = GetSelectedIntOrNull(AdrBox != null ? AdrBox.SelectedValue : null);
                    if (adrId == null)
                    {
                        MessageBox.Show("Bitte eine bestehende Adresse wählen oder 'Neue Adresse anlegen' aktivieren.", "Anlernen",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var adr = _db.LadeAdresseById(adrId.Value);

                    if (istEinnahme && budgetCheck != null && budgetCheck.IsChecked == true)
                    {
                        adr.IstBudgetiert = true;

                        // WICHTIG:
                        // Bestehendes Standardkonto bleibt Standard.
                        // Abweichendes Konto wird als Sonderregel gelernt.
                        if (!adr.StandardEinnahmenKontoId.HasValue || adr.StandardEinnahmenKontoId.Value <= 0)
                        {
                            adr.StandardEinnahmenKontoId = kontoId;
                            _db.AktualisiereAdresse(adr);
                        }
                        else if (adr.StandardEinnahmenKontoId.Value != kontoId.Value)
                        {
                            regelSpeichern = true;
                            regelKontoId = kontoId.Value;
                        }
                    }
                    else
                    {
                        // Ausgabe / Refund:
                        // Bestehendes DefaultKonto bleibt Standard.
                        // Abweichendes Konto wird als Sonderregel gelernt.
                        if (!adr.DefaultKontoId.HasValue || adr.DefaultKontoId.Value <= 0)
                        {
                            adr.DefaultKontoId = kontoId;
                            _db.AktualisiereAdresse(adr);
                        }
                        else if (adr.DefaultKontoId.Value != kontoId.Value)
                        {
                            regelSpeichern = true;
                            regelKontoId = kontoId.Value;
                        }
                    }
                }

                SelectedAdresseId = adrId;
                SelectedKontoId = kontoId;

                // ---------------------------------------------
                // Bestehendes Verhalten:
                // Alias für Adress-Erkennung anlegen
                // ---------------------------------------------
                if (SelectedAdresseId.HasValue && !string.IsNullOrWhiteSpace(_item.CounterpartyName))
                    _db.SpeichereAdressAlias(SelectedAdresseId.Value, _item.CounterpartyName.Trim(), "Exact");

                if (SelectedAdresseId.HasValue)
                {
                    var cand = BuildAliasCandidate(_item.Text, _item.ServiceRef);
                    if (!string.IsNullOrWhiteSpace(cand))
                        _db.SpeichereAdressAlias(SelectedAdresseId.Value, cand, "Contains");
                }

                // ---------------------------------------------
                // NEU:
                // Sonderregel nur dann speichern, wenn bestehende
                // Adresse bereits ein anderes Standardkonto hat.
                // ---------------------------------------------
                if (SelectedAdresseId.HasValue && regelSpeichern && regelKontoId.HasValue)
                {
                    var regelText = BuildAliasCandidate(_item.Text, _item.ServiceRef);

                    if (string.IsNullOrWhiteSpace(regelText))
                        regelText = string.IsNullOrWhiteSpace(_item.Text) ? null : _item.Text.Trim();

                    if (!string.IsNullOrWhiteSpace(regelText))
                    {
                        var betragAbs = Math.Abs(_item.Amount);

                        _db.SpeichereAdressBuchungsregel(
                            adresseId: SelectedAdresseId.Value,
                            istEinnahme: istEinnahme,
                            textPattern: regelText,
                            patternModus: "Contains",
                            kontoId: regelKontoId.Value,
                            betragVon: betragAbs,
                            betragBis: betragAbs,
                            prioritaet: 100
                        );
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Anlernen fehlgeschlagen:\n" + ex.Message, "Anlernen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------- Vorbelegung Adresse & Konto (wieder aufgenommen) ----------------
        private void PreselectAdresseUndKonto()
        {
            bool istEinnahme = _item.Direction == KreditDebit.Credit;
            bool adresseGefunden = false;

            // 1) Vorschlag aus Erkenner
            if (_item.VorschlagAdresseId.HasValue)
            {
                AdrBox.SelectedValue = _item.VorschlagAdresseId.Value;
                adresseGefunden = true;
            }

            // 2) Fallback: IBAN der Gegenpartei
            if (!adresseGefunden && !string.IsNullOrWhiteSpace(_item.CounterpartyIban))
            {
                var src = AdrBox.ItemsSource as IEnumerable<Adresse>;
                if (src != null)
                {
                    string wantIban = _item.CounterpartyIban.Replace(" ", "");
                    var adrByIban = src.FirstOrDefault(a =>
                        !string.IsNullOrWhiteSpace(a.IBAN) &&
                        string.Equals(a.IBAN.Replace(" ", ""), wantIban, StringComparison.OrdinalIgnoreCase));
                    if (adrByIban != null)
                    {
                        AdrBox.SelectedValue = adrByIban.Id;
                        adresseGefunden = true;
                    }
                }
            }

            // 3) Fallback: exakter Namensvergleich
            if (!adresseGefunden && !string.IsNullOrWhiteSpace(_item.CounterpartyName))
            {
                var src = AdrBox.ItemsSource as IEnumerable<Adresse>;
                if (src != null)
                {
                    string wanted = _item.CounterpartyName.Trim();
                    var adrByName = src.FirstOrDefault(a =>
                        string.Equals(a.Name != null ? a.Name.Trim() : null,
                                      wanted,
                                      StringComparison.CurrentCultureIgnoreCase));
                    if (adrByName != null)
                    {
                        AdrBox.SelectedValue = adrByName.Id;
                        adresseGefunden = true;
                    }
                }
            }

            // 4) Checkbox & Felder passend setzen
            if (adresseGefunden)
            {
                NeueAdresseCheck.IsChecked = false;
                NeuNameBox.Text = "";
                NeuIbanBox.Text = "";
            }
            else
            {
                NeueAdresseCheck.IsChecked = true;
                NeuNameBox.Text = string.IsNullOrWhiteSpace(_item.CounterpartyName) ? "" : _item.CounterpartyName.Trim();
                NeuIbanBox.Text = string.IsNullOrWhiteSpace(_item.CounterpartyIban) ? "" : _item.CounterpartyIban.Trim();
            }

            // 5) Konto-Vorwahl
            int? presetKonto = _item.VorschlagNachKontoId;

            if (AdrBox.SelectedValue is int adrIdSel)
            {
                var adrSel = _db.LadeAdresseById(adrIdSel);

                if (!presetKonto.HasValue)
                {
                    if (istEinnahme && adrSel != null && adrSel.IstBudgetiert && adrSel.StandardEinnahmenKontoId.HasValue)
                        presetKonto = adrSel.StandardEinnahmenKontoId.Value;
                    else if (!istEinnahme && adrSel != null && adrSel.DefaultKontoId.HasValue)
                        presetKonto = adrSel.DefaultKontoId.Value;
                }

                // Hinweis-Checkbox nur als visueller Hinweis setzen (sichtbar ist sie immer)
                if (istEinnahme && adrSel != null && adrSel.IstBudgetiert && BudgetEinnahmenCheck != null)
                    BudgetEinnahmenCheck.IsChecked = true;
            }

            if (presetKonto.HasValue)
                KontoBox.SelectedValue = presetKonto.Value;
        }

        // ---------------- Alias-Helfer ----------------
        private static readonly HashSet<string> _aliasStop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RECHNUNG","REFERENZ","ZAHLUNG","GEBUEHR","KARTENZAHLUNG","BELASTUNG",
            "GUTSCHRIFT","MITTEILUNG","VALUTA","SEPA","SWIFT","UETR","CHF","EUR","USD",
            "VISA","MASTERCARD","TWINT","POSTFINANCE","UBS","CS","BANK","KONTO","IBAN"
        };

        private static string BuildAliasCandidate(string text, string serviceRef)
        {
            var src = !string.IsNullOrWhiteSpace(text) ? text : (serviceRef ?? "");
            if (string.IsNullOrWhiteSpace(src)) return null;

            // IBANs / lange Nummern entfernen
            string t = Regex.Replace(src, @"[A-Z]{2}\d{2}[A-Z0-9]{4,}", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b\d{5,}\b", " ");

            // Wörter extrahieren (>=3 Zeichen), Stopwörter raus
            var words = Regex.Matches(t.ToUpperInvariant(), @"[A-ZÄÖÜ0-9]{3,}")
                             .Cast<Match>().Select(m => m.Value)
                             .Where(w => !_aliasStop.Contains(w))
                             .ToList();
            if (words.Count == 0) return null;

            var picks = words.Take(4).Select(w => w.Length <= 5 ? w : w.Substring(0, 5)).ToList();
            var code = string.Join("-", picks);

            if (code.Replace("-", "").Length < 8)
            {
                var fallback = words.OrderByDescending(w => w.Length).Take(2)
                                    .Select(w => w.Length <= 6 ? w : w.Substring(0, 6));
                code = string.Join("-", fallback);
            }

            return code;
        }

        // ===== Hinweis Budgetzeitraum =====
        private void TryShowBudgetHint()
        {
            try
            {
                if (BudgetHint == null || BudgetHintText == null) return;

                BudgetHint.Visibility = Visibility.Collapsed;
                BudgetHintText.Text = string.Empty;

                DateTime dt = _item.BookingDate.Date;

                var period = GetActiveBudgetPeriod();
                if (period == null) return;

                DateTime start = period.Item1.Date;
                DateTime end = period.Item2.Date;

                if (dt < start || dt > end)
                {
                    BudgetHint.Visibility = Visibility.Visible;
                    BudgetHintText.Text =
                        $"Hinweis: Diese Buchung ({dt:dd.MM.yyyy}) liegt außerhalb vom aktiven Budgetzeitraum";
                }
            }
            catch { /* still */ }
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


        // ===== Robust & ohne Nullable-Pattern =====
        private static int? GetSelectedIntOrNull(object value)
        {
            if (value == null || value == DBNull.Value) return null;

            try
            {
                // typ. int oder string in ComboBox.SelectedValue
                if (value is int) return (int)value;
                if (value is long) return Convert.ToInt32((long)value);
                if (value is string)
                {
                    int parsed;
                    if (int.TryParse((string)value, out parsed))
                        return parsed;
                    return null;
                }
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }
    }
}
