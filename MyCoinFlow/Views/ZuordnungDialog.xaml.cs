using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using MyCoinFlow.Import;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Text.RegularExpressions;


namespace MyCoinFlow.Views
{
    public partial class ZuordnungDialog : Window
    {
        private readonly DatabaseService _db = new();
        private readonly BankImportItem _item;

        // Rückgaben
        public int? SelectedAdresseId { get; private set; }
        public int? SelectedKontoId { get; private set; }

        public ZuordnungDialog(BankImportItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            InitializeComponent();
            InitUi();
        }

        private void InitUi()
        {
            // Buchungsinfo
            DataContext = new
            {
                BuchungInfo = $"{_item.BookingDate:yyyy-MM-dd}  |  {_item.Amount:N2} {_item.Currency}  |  {(_item.Direction == KreditDebit.Debit ? "Ausgabe" : "Einnahme")}",
                BuchungText = string.IsNullOrWhiteSpace(_item.Text) ? "(kein Buchungstext)" : _item.Text
            };

            // Adressen laden
            AdrBox.ItemsSource = _db.LadeAdressen().OrderBy(a => a.Name).ToList();
            AdrBox.SelectedIndex = -1;

            // Konten laden
            KontoBox.ItemsSource = _db.LadeKontoLookup(); // {Id, Anzeige}
            if (KontoBox.Items.Count > 0) KontoBox.SelectedIndex = 0;

            // Geldinstitut Info
            string giText = "unbekannt";
            if (!string.IsNullOrWhiteSpace(_item.AccountIban))
                giText = $"Konto-IBAN: {_item.AccountIban}";
            GiInfoText.Text = giText;

            // Defaults aus Gegenpartei (Name)
            if (!string.IsNullOrWhiteSpace(_item.CounterpartyName))
            {
                var adrByName = ((IEnumerable<Adresse>)AdrBox.ItemsSource).FirstOrDefault(a =>
                    string.Equals(a.Name?.Trim(), _item.CounterpartyName.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (adrByName != null)
                    AdrBox.SelectedValue = adrByName.Id;
            }

            // --- IBAN aus der Transaktion in die Maske übernehmen ---
            if (!string.IsNullOrWhiteSpace(_item.CounterpartyIban))
            {
                var adrByIban = ((IEnumerable<Adresse>)AdrBox.ItemsSource).FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.IBAN) &&
                    string.Equals(a.IBAN.Replace(" ", ""), _item.CounterpartyIban.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

                if (adrByIban != null)
                {
                    AdrBox.SelectedValue = adrByIban.Id;
                    NeueAdresseCheck.IsChecked = false;
                    NeuIbanBox.Text = "";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(NeuIbanBox.Text))
                        NeuIbanBox.Text = _item.CounterpartyIban.Trim();
                    if (AdrBox.SelectedValue == null)
                        NeueAdresseCheck.IsChecked = true;
                }
            }

            // --- VORBELEGUNG aus erkannten Vorschlägen ---
            if (_item.VorschlagAdresseId.HasValue)
            {
                AdrBox.SelectedValue = _item.VorschlagAdresseId.Value;
                NeueAdresseCheck.IsChecked = false;
                NeuNameBox.Text = "";
                NeuIbanBox.Text = "";
            }
            else if (!string.IsNullOrWhiteSpace(_item.CounterpartyName))
            {
                var adr = ((IEnumerable<Adresse>)AdrBox.ItemsSource).FirstOrDefault(a =>
                    string.Equals(a.Name?.Trim(), _item.CounterpartyName.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (adr != null) AdrBox.SelectedValue = adr.Id;
            }

            // ---- NEU: Budget-UI (sichtbar nur bei Einnahmen) + Konto-Vorwahl nach Richtung ----
            var budgetCheck = this.FindName("BudgetEinnahmenCheck") as System.Windows.Controls.CheckBox;
            var budgetHint = this.FindName("BudgetHinweisText") as System.Windows.Controls.TextBlock;
            bool istEinnahme = _item.Direction == KreditDebit.Credit;

            if (budgetCheck != null) budgetCheck.Visibility = istEinnahme ? Visibility.Visible : Visibility.Collapsed;
            if (budgetHint != null) budgetHint.Visibility = istEinnahme ? Visibility.Visible : Visibility.Collapsed;

            // Konto vorbelegen:
            // 1) vorhandener Vorschlag
            // 2) bei Einnahme: StandardEinnahmenKonto der Adresse
            //    bei Ausgabe:  DefaultKonto der Adresse
            int? presetKonto = _item.VorschlagNachKontoId;

            if (_item.VorschlagAdresseId.HasValue)
            {
                var adrSel = _db.LadeAdresseById(_item.VorschlagAdresseId.Value);

                if (!presetKonto.HasValue)
                {
                    if (istEinnahme)
                        presetKonto = adrSel?.StandardEinnahmenKontoId ?? presetKonto;
                    else
                        presetKonto = adrSel?.DefaultKontoId ?? presetKonto;
                }

                // Wenn Adresse bereits budgetiert ist: Checkbox setzen + Konto übernehmen
                if (istEinnahme && adrSel?.IstBudgetiert == true && budgetCheck != null)
                {
                    budgetCheck.IsChecked = true;
                    if (adrSel.StandardEinnahmenKontoId.HasValue && KontoBox.SelectedValue == null)
                        KontoBox.SelectedValue = adrSel.StandardEinnahmenKontoId.Value;
                }
            }

            if (presetKonto.HasValue)
                KontoBox.SelectedValue = presetKonto.Value;
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int? kontoId = KontoBox.SelectedValue as int?;
                if (kontoId == null)
                {
                    MessageBox.Show("Bitte ein Standardkonto wählen.", "Anlernen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool istEinnahme = _item.Direction == KreditDebit.Credit;
                var budgetCheck = this.FindName("BudgetEinnahmenCheck") as System.Windows.Controls.CheckBox;

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

                    var adr = new Adresse
                    {
                        Name = name,
                        IBAN = iban
                    };

                    // 2A) Neuanlage: je nach Fall Standard-Einnahmenkonto ODER DefaultKonto setzen
                    if (istEinnahme && budgetCheck?.IsChecked == true)
                    {
                        // Echte Einnahmen-Adresse
                        adr.IstBudgetiert = true;
                        adr.StandardEinnahmenKontoId = kontoId;
                        adr.DefaultKontoId = null;
                    }
                    else
                    {
                        // Normale Debit-Adresse (auch wenn diese Buchung eine Gutschrift ist → Refund)
                        adr.IstBudgetiert = false;
                        adr.StandardEinnahmenKontoId = null;
                        adr.DefaultKontoId = kontoId;
                    }

                    adrId = _db.SpeichereAdresse(adr);
                }
                else
                {
                    // Bestehende Adresse
                    adrId = AdrBox.SelectedValue as int?;
                    if (adrId == null)
                    {
                        MessageBox.Show("Bitte eine bestehende Adresse wählen oder 'Neue Adresse anlegen' aktivieren.", "Anlernen",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var adr = _db.LadeAdresseById(adrId.Value);

                    // 2B) Bestehend: Budgetierte Einnahmen vs. Refund (DefaultKonto)
                    if (istEinnahme && budgetCheck?.IsChecked == true)
                    {
                        // Echte Einnahmen-Adresse
                        adr.IstBudgetiert = true;
                        adr.StandardEinnahmenKontoId = kontoId;
                        // DefaultKontoId bewusst NICHT anfassen
                        _db.AktualisiereAdresse(adr);
                    }
                    else
                    {
                        // Refund / normale Debit-Adresse → DefaultKonto setzen/aktualisieren
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
                {
                    _db.SpeichereAdressAlias(SelectedAdresseId.Value, _item.CounterpartyName.Trim(), "Exact");
                }
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


        // Stopwörter weglassen (Rauschen)
        private static readonly HashSet<string> _aliasStop = new(StringComparer.OrdinalIgnoreCase)
{
    "RECHNUNG","REFERENZ","ZAHLUNG","GEBUEHR","KARTENZAHLUNG","BELASTUNG",
    "GUTSCHRIFT","MITTEILUNG","VALUTA","SEPA","SWIFT","UETR","CHF","EUR","USD",
    "VISA","MASTERCARD","TWINT","POSTFINANCE","UBS","CS","BANK","KONTO","IBAN"
};

        // Baut einen stabilen, kurzen Contains-Alias aus Text/ServiceRef
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

            // Kompakten Code bilden, z.B. EINK-TWIN-BRAC(K) … erste 3–4 Wörter, jeweils 4–6 Zeichen
            var picks = words.Take(4)
                             .Select(w => w.Length <= 5 ? w : w[..5])
                             .ToList();

            var code = string.Join("-", picks);

            // Fallback, falls zu kurz
            if (code.Replace("-", "").Length < 8)
            {
                var fallback = words.OrderByDescending(w => w.Length).Take(2)
                                    .Select(w => w.Length <= 6 ? w : w[..6]);
                code = string.Join("-", fallback);
            }

            return code;
        }
    }

    // Kleiner Helfer für XAML-Binding (Negation) – wird in XAML als {x:Static views:BooleanNegationConverter.Instance} verwendet.
    public sealed class BooleanNegationConverter : IValueConverter
    {
        public static readonly BooleanNegationConverter Instance = new BooleanNegationConverter();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
    }
}
