using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class ZuordnungDialog : Window
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
            InitUi();
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
            KontoBox.ItemsSource = _db.LadeKontoLookup();                             // {Id, Anzeige}

            // 3) Geldinstitut-Info
            GiInfoText.Text = !string.IsNullOrWhiteSpace(_item.AccountIban)
                ? $"Konto-IBAN: {_item.AccountIban}"
                : "unbekannt";

            // 4) Vorbelegung: per Vorschlag/IBAN/Name
            PreselectAdresseUndKonto();

            // 5) Erste Regel-Preview zeigen
            UpdateRulePreview();
        }

        /// <summary>
        /// Bei erkannten Buchungen ist "Neue Adresse anlegen" AUS. Nur ohne Treffer EIN.
        /// </summary>
        private void PreselectAdresseUndKonto()
        {
            bool istEinnahme = _item.Direction == KreditDebit.Credit;
            bool adresseGefunden = false;

            // 1) Direkt über Vorschlag (vom Erkenner)
            if (_item.VorschlagAdresseId.HasValue)
            {
                AdrBox.SelectedValue = _item.VorschlagAdresseId.Value;
                adresseGefunden = true;
            }

            // 2) Fallback: IBAN der Gegenpartei
            if (!adresseGefunden && !string.IsNullOrWhiteSpace(_item.CounterpartyIban))
            {
                var adrByIban = (AdrBox.ItemsSource as IEnumerable<Adresse>)!
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.IBAN) &&
                                         string.Equals(a.IBAN.Replace(" ", ""),
                                                       _item.CounterpartyIban.Replace(" ", ""),
                                                       StringComparison.OrdinalIgnoreCase));
                if (adrByIban != null)
                {
                    AdrBox.SelectedValue = adrByIban.Id;
                    adresseGefunden = true;
                }
            }

            // 3) Fallback: exakter Namensvergleich
            if (!adresseGefunden && !string.IsNullOrWhiteSpace(_item.CounterpartyName))
            {
                var adrByName = (AdrBox.ItemsSource as IEnumerable<Adresse>)!
                    .FirstOrDefault(a => string.Equals(a.Name?.Trim(),
                                                       _item.CounterpartyName.Trim(),
                                                       StringComparison.CurrentCultureIgnoreCase));
                if (adrByName != null)
                {
                    AdrBox.SelectedValue = adrByName.Id;
                    adresseGefunden = true;
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
                    if (istEinnahme && adrSel?.IstBudgetiert == true && adrSel.StandardEinnahmenKontoId.HasValue)
                        presetKonto = adrSel.StandardEinnahmenKontoId.Value;
                    else if (!istEinnahme && adrSel?.DefaultKontoId.HasValue == true)
                        presetKonto = adrSel.DefaultKontoId.Value;
                }

                // Einnahmen-Checkbox nur als visueller Hinweis setzen
                if (istEinnahme && adrSel?.IstBudgetiert == true && BudgetEinnahmenCheck != null)
                    BudgetEinnahmenCheck.IsChecked = true;
            }

            if (presetKonto.HasValue)
                KontoBox.SelectedValue = presetKonto.Value;
        }

        // ------------ Regel-Preview (nur Anzeige) ------------
        private void UpdateRulePreview()
        {
            // Adresse bestimmen (bestehend)
            Adresse? adr = null;
            if (NeueAdresseCheck.IsChecked != true && AdrBox.SelectedValue is int adrId)
            {
                try { adr = _db.LadeAdresseById(adrId); } catch { adr = null; }
            }

            bool istUmbuchung = IstUmbuchungsAdresseName(adr?.Name);
            bool istEinnahme = _item.Direction == KreditDebit.Credit;
            bool budgetFlag = BudgetEinnahmenCheck?.IsChecked == true;

            // Kontotext robust ermitteln
            string kontoLabel = "(Konto auswählen)";
            if (KontoBox?.SelectedItem != null)
            {
                var pi = KontoBox.SelectedItem.GetType().GetProperty("Anzeige");
                kontoLabel = Convert.ToString(pi?.GetValue(KontoBox.SelectedItem)) ?? kontoLabel;
            }

            // Ableitung analog deiner Regeln:
            PackIconKind icon;
            string typText;
            string hint;

            if (!istEinnahme) // DBIT
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
            else // CRDT
            {
                if (NeueAdresseCheck.IsChecked == true ? budgetFlag : (adr?.IstBudgetiert == true && adr.StandardEinnahmenKontoId.HasValue))
                {
                    icon = PackIconKind.AccountCash;
                    typText = "Adresse → Bank (Einnahme)";
                    hint = $"Nach-Konto (Standard-Einnahmenkonto): {kontoLabel}";
                }
                else
                {
                    icon = PackIconKind.Bank;
                    typText = "Konto → Bank (Refund)";
                    hint = $"Von-Konto (DefaultKonto der Adresse): {kontoLabel}";
                }
            }

            TypIcon.Kind = icon;
            TypText.Text = typText;
            TypHint.Text = hint;
        }

        private static bool IstUmbuchungsAdresseName(string? name)
            => !string.IsNullOrWhiteSpace(name) &&
               name.Trim().StartsWith("Interne Umbuchung", StringComparison.CurrentCultureIgnoreCase);

        // Events, die die Preview betreffen
        private void UiChanged(object? sender, RoutedEventArgs e) => UpdateRulePreview();

        // ------------ Buttons ------------
        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int? kontoId = GetSelectedIntOrNull(KontoBox?.SelectedValue);
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
                if (NeueAdresseCheck.IsChecked == true)
                {
                    var name = (NeuNameBox.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Bitte einen Namen für die neue Adresse eingeben.", "Anlernen",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        NeuNameBox.Focus();
                        return;
                    }
                    var ibanRaw = NeuIbanBox.Text?.Trim();
                    string? iban = string.IsNullOrWhiteSpace(ibanRaw) ? null : ibanRaw.Replace(" ", "").ToUpperInvariant();

                    var adrNeu = new Adresse { Name = name, IBAN = iban };

                    // Neuanlage: je nach Fall Standard-Einnahmenkonto ODER DefaultKonto setzen
                    if (istEinnahme && budgetCheck?.IsChecked == true)
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
                    // Bestehende Adresse
                    adrId = GetSelectedIntOrNull(AdrBox?.SelectedValue);
                    if (adrId == null)
                    {
                        MessageBox.Show("Bitte eine bestehende Adresse wählen oder 'Neue Adresse anlegen' aktivieren.", "Anlernen",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var adr = _db.LadeAdresseById(adrId.Value);

                    // Bestehend: Budgetierte Einnahmen vs. Refund (DefaultKonto)
                    if (istEinnahme && budgetCheck?.IsChecked == true)
                    {
                        adr.IstBudgetiert = true;
                        adr.StandardEinnahmenKontoId = kontoId;
                        _db.AktualisiereAdresse(adr);
                    }
                    else
                    {
                        if (adr.DefaultKontoId != kontoId)
                        {
                            adr.DefaultKontoId = kontoId;
                            _db.AktualisiereAdresse(adr);
                        }
                    }
                }

                // Rückgaben an den Aufrufer
                SelectedAdresseId = adrId;
                SelectedKontoId = kontoId;

                // Aliase automatisch anlegen
                if (SelectedAdresseId.HasValue && !string.IsNullOrWhiteSpace(_item.CounterpartyName))
                    _db.SpeichereAdressAlias(SelectedAdresseId.Value, _item.CounterpartyName.Trim(), "Exact");

                if (SelectedAdresseId.HasValue)
                {
                    var cand = BuildAliasCandidate(_item.Text, _item.ServiceRef);
                    if (!string.IsNullOrWhiteSpace(cand))
                        _db.SpeichereAdressAlias(SelectedAdresseId.Value, cand!, "Contains");
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

        // ---------------- Alias-Helfer ----------------
        private static readonly HashSet<string> _aliasStop = new(StringComparer.OrdinalIgnoreCase)
        {
            "RECHNUNG","REFERENZ","ZAHLUNG","GEBUEHR","KARTENZAHLUNG","BELASTUNG",
            "GUTSCHRIFT","MITTEILUNG","VALUTA","SEPA","SWIFT","UETR","CHF","EUR","USD",
            "VISA","MASTERCARD","TWINT","POSTFINANCE","UBS","CS","BANK","KONTO","IBAN"
        };

        private static string? BuildAliasCandidate(string? text, string? serviceRef)
        {
            var src = !string.IsNullOrWhiteSpace(text) ? text! : (serviceRef ?? "");
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

            var picks = words.Take(4).Select(w => w.Length <= 5 ? w : w[..5]).ToList();
            var code = string.Join("-", picks);

            // <-- FIX: Replace statt replace
            if (code.Replace("-", "").Length < 8)
            {
                var fallback = words.OrderByDescending(w => w.Length).Take(2)
                                    .Select(w => w.Length <= 6 ? w : w[..6]);
                code = string.Join("-", fallback);
            }

            return code;
        }

        private static int? GetSelectedIntOrNull(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }
    }
}
