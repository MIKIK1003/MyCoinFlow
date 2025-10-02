using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Zeigt Transaktionen zu einer Adresse – mit Filter, Summen und Spalten Einnahmen/Ausgaben.
    /// Aufbau analog Konto-/Geldinstitut-Transaktionen.
    /// </summary>
    public sealed class AdresseTransaktionenViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly int _adrId;
        public string Titel { get; }

        // Filter
        private DateTime? _filterVon;
        public DateTime? FilterVon { get => _filterVon; set { _filterVon = value; OnPropertyChanged(); } }

        private DateTime? _filterBis;
        public DateTime? FilterBis { get => _filterBis; set { _filterBis = value; OnPropertyChanged(); } }

        private decimal? _filterMinBetrag;
        public decimal? FilterMinBetrag { get => _filterMinBetrag; set { _filterMinBetrag = value; OnPropertyChanged(); } }

        private decimal? _filterMaxBetrag;
        public decimal? FilterMaxBetrag { get => _filterMaxBetrag; set { _filterMaxBetrag = value; OnPropertyChanged(); } }

        private int? _filterKontoId;
        public int? FilterKontoId { get => _filterKontoId; set { _filterKontoId = value; OnPropertyChanged(); } }

        private int? _filterGeldinstitutId;
        public int? FilterGeldinstitutId { get => _filterGeldinstitutId; set { _filterGeldinstitutId = value; OnPropertyChanged(); } }

        // Auswahllisten (für Combos)
        public ObservableCollection<KontoLookup> KontenLookup { get; } = new();
        public ObservableCollection<Geldinstitut> Geldinstitute { get; } = new();

        // Datenzeilen für das Grid
        public ObservableCollection<Row> Rows { get; } = new();

        // Commands
        public ICommand ApplyFilterCommand { get; }
        public ICommand ResetFilterCommand { get; }

        public string SummaryText
        {
            get
            {
                var ein = Rows.Sum(r => r.Einnahmen);
                var aus = Rows.Sum(r => r.Ausgaben);
                var saldo = ein - aus;
                return $"Einnahmen {ein:N2}   |   Ausgaben {aus:N2}   |   Saldo {saldo:N2}";
            }
        }

        // Konto-Anzeige ohne Klassifikation „[Art/Gruppe]“
        private System.Collections.Generic.Dictionary<int, string> _kontoLabel = new();

        // Set aller Ertragskonten zur Bewertung von Ein/Aus (wie in GI-/Konto-VM)
        private readonly System.Collections.Generic.HashSet<int> _incomeAccounts;

        public AdresseTransaktionenViewModel(int adresseId, string adresseName)
        {
            _adrId = adresseId;
            Titel = $"Transaktionen – {adresseName} (Adresse)";

            ApplyFilterCommand = new RelayCommand(_ => Load(), _ => true);
            ResetFilterCommand = new RelayCommand(_ =>
            {
                FilterVon = null; FilterBis = null;
                FilterMinBetrag = null; FilterMaxBetrag = null;
                FilterKontoId = null; FilterGeldinstitutId = null;
                Load();
            });

            // Lookup-Listen laden
            foreach (var k in _db.LadeKontoLookup()) KontenLookup.Add(k);
            _kontoLabel = KontenLookup.ToDictionary(k => k.Id, k =>
            {
                // "1234 Detail [Art/Gruppe/UG]" -> "1234 Detail"
                var anzeige = k.Anzeige ?? "";
                var idx = anzeige.IndexOf(" [", StringComparison.Ordinal);
                return idx >= 0 ? anzeige[..idx].Trim() : anzeige.Trim();
            });

            foreach (var gi in _db.LadeGeldinstitute()) Geldinstitute.Add(gi);

            _incomeAccounts = BuildIncomeAccountSet();

            Load();
        }

        private System.Collections.Generic.HashSet<int> BuildIncomeAccountSet()
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (var k in _db.LadeKontenplan())
            {
                bool isIncome =
                       (k.Kontonummer >= 3000 && k.Kontonummer <= 3999)  // Ertragskonten
                    || (k.Kontonummer >= 7000 && k.Kontonummer <= 7999)  // weitere Erträge
                    || ContainsIncome(k.Art) || ContainsIncome(k.Gruppe)
                    || ContainsIncome(k.Untergruppe) || ContainsIncome(k.Detail);

                if (isIncome) set.Add(k.Id);
            }
            return set;

            static bool ContainsIncome(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var u = s.ToUpperInvariant();
                return u.Contains("EINNAHM") || u.Contains("ERTR") || u.Contains("INCOME") || u.Contains("REVENUE");
            }
        }

        private void Load()
        {
            Rows.Clear();

            var list = _db.LadeTransaktionenByAdresse(
                _adrId, FilterVon, FilterBis, FilterMinBetrag, FilterMaxBetrag, FilterKontoId, FilterGeldinstitutId);

            foreach (var t in list.OrderByDescending(t => t.Datum).ThenByDescending(t => t.Id))
            {
                // Kontoanzeige (VON bevorzugt, sonst NACH), Label ohne Klassifikation
                string konto =
                      (t.VonKontoId.HasValue && _kontoLabel.TryGetValue(t.VonKontoId.Value, out var vLbl)) ? vLbl
                    : (t.NachKontoId.HasValue && _kontoLabel.TryGetValue(t.NachKontoId.Value, out var nLbl)) ? nLbl
                    : "Konto";

                // --- Einnahmen/Ausgaben bestimmen ---
                // Falls deine Transaktion ein Feld "DebitCredit" (DEBIT/CREDIT) besitzt, kannst du hier
                // bevorzugt auswerten. Andernfalls verwenden wir analog Konto/Geldinstitut die Konto-Logik:
                decimal einnahmen = 0m, ausgaben = 0m;

                // Prefer explicit Debit/Credit if available:
                // (Unkommentieren, falls dein Modell z.B. t.DebitCredit als string hat)
                /*
                if (!string.IsNullOrWhiteSpace(t.DebitCredit))
                {
                    var dc = t.DebitCredit.Trim().ToUpperInvariant();
                    if (dc == "CREDIT") einnahmen = Math.Abs(t.Betrag);
                    else if (dc == "DEBIT") ausgaben = Math.Abs(t.Betrag);
                    else // Fallback
                    {
                        if (t.NachKontoId.HasValue && _incomeAccounts.Contains(t.NachKontoId.Value))
                            einnahmen = t.Betrag;
                        else
                            ausgaben = t.Betrag;
                    }
                }
                else
                */
                {
                    // Fallback (bewährt in deinen bestehenden Views):
                    if (t.NachKontoId.HasValue)
                    {
                        // Zahlung Richtung Budget-Konto
                        if (_incomeAccounts.Contains(t.NachKontoId.Value))
                            einnahmen = t.Betrag;   // Ertragskonto → Einnahme
                        else
                            ausgaben = t.Betrag;    // Aufwand/sonst → Ausgabe
                    }
                    else
                    {
                        // Ohne NachKonto → i.d.R. Bank-only mit Adresse: als Einnahme behandeln
                        if (t.VonKontoId.HasValue || t.GeldinstitutId.HasValue)
                            einnahmen = t.Betrag;
                    }
                }

                Rows.Add(new Row
                {
                    Id = t.Id,
                    Datum = t.Datum,
                    Konto = konto,
                    Einnahmen = einnahmen > 0 ? einnahmen : 0m,
                    Ausgaben = ausgaben > 0 ? ausgaben : 0m,
                    GeldinstitutName = t.BankName,
                    Notiz = t.Notiz
                });
            }

            OnPropertyChanged(nameof(SummaryText));
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // --- Row für das Grid ---
        public sealed class Row
        {
            public int Id { get; set; }
            public DateTime Datum { get; set; }
            public string Konto { get; set; } = "";
            public decimal Einnahmen { get; set; }
            public decimal Ausgaben { get; set; }
            public string? GeldinstitutName { get; set; }
            public string? Notiz { get; set; }
        }
    }
}
