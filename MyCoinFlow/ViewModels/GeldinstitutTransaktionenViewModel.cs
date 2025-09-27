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
    public sealed class GeldinstitutTransaktionenViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly int _giId;
        public string Titel { get; }

        // Filter
        public DateTime? FilterVon { get => _filterVon; set { _filterVon = value; OnPropertyChanged(); } }
        private DateTime? _filterVon;

        public DateTime? FilterBis { get => _filterBis; set { _filterBis = value; OnPropertyChanged(); } }
        private DateTime? _filterBis;

        public decimal? FilterMinBetrag { get => _filterMinBetrag; set { _filterMinBetrag = value; OnPropertyChanged(); } }
        private decimal? _filterMinBetrag;

        public decimal? FilterMaxBetrag { get => _filterMaxBetrag; set { _filterMaxBetrag = value; OnPropertyChanged(); } }
        private decimal? _filterMaxBetrag;

        public int? FilterAdresseId { get => _filterAdresseId; set { _filterAdresseId = value; OnPropertyChanged(); } }
        private int? _filterAdresseId;

        public int? FilterKontoId { get => _filterKontoId; set { _filterKontoId = value; OnPropertyChanged(); } }
        private int? _filterKontoId;

        // Auswahllisten
        public ObservableCollection<Adresse> Adressen { get; } = new();
        public ObservableCollection<KontoLookup> KontenLookup { get; } = new();

        // Rows
        public ObservableCollection<Row> Rows { get; } = new();

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

        private readonly System.Collections.Generic.HashSet<int> _incomeAccounts;
        private System.Collections.Generic.Dictionary<int, string> _kontoLabel = new();

        public GeldinstitutTransaktionenViewModel(int geldinstitutId, string geldinstitutName)
        {
            _giId = geldinstitutId;
            Titel = $"Transaktionen – {geldinstitutName}";

            ApplyFilterCommand = new RelayCommand(_ => Load(), _ => true);
            ResetFilterCommand = new RelayCommand(_ =>
            {
                FilterVon = null; FilterBis = null;
                FilterMinBetrag = null; FilterMaxBetrag = null;
                FilterAdresseId = null; FilterKontoId = null;
                Load();
            });

            foreach (var a in _db.LadeAdressen().OrderBy(a => a.Name)) Adressen.Add(a);
            foreach (var k in _db.LadeKontoLookup()) KontenLookup.Add(k);
            _kontoLabel = KontenLookup.ToDictionary(k => k.Id, k => StripClassification(k.Anzeige));

            _incomeAccounts = BuildIncomeAccountSet();

            Load();
        }

        private static string StripClassification(string anzeige)
        {
            // "1234 Detail [Art/Gruppe/UG]" -> "1234 Detail"
            if (string.IsNullOrWhiteSpace(anzeige)) return "";
            var idx = anzeige.IndexOf(" [", StringComparison.Ordinal);
            return idx >= 0 ? anzeige[..idx].Trim() : anzeige.Trim();
        }

        private System.Collections.Generic.HashSet<int> BuildIncomeAccountSet()
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (var k in _db.LadeKontenplan())
            {
                bool isIncome =
                       (k.Kontonummer >= 3000 && k.Kontonummer <= 3999)
                    || (k.Kontonummer >= 7000 && k.Kontonummer <= 7999)
                    || ContainsIncome(k.Art)
                    || ContainsIncome(k.Gruppe)
                    || ContainsIncome(k.Untergruppe)
                    || ContainsIncome(k.Detail);

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

            var list = _db.LadeTransaktionenByGeldinstitut(
                _giId, FilterVon, FilterBis, FilterMinBetrag, FilterMaxBetrag, FilterAdresseId, FilterKontoId);

            foreach (var t in list.OrderByDescending(t => t.Datum).ThenByDescending(t => t.Id))
            {
                // Konto: bevorzugt VON, dann NACH, sonst Bankname – Anzeige ohne Klassifikation
                string konto =
                      (t.VonKontoId.HasValue && _kontoLabel.TryGetValue(t.VonKontoId.Value, out var vLbl)) ? vLbl
                    : (t.NachKontoId.HasValue && _kontoLabel.TryGetValue(t.NachKontoId.Value, out var nLbl)) ? nLbl
                    : (t.BankName ?? "Bank");

                // Einnahmen/Ausgaben bankseitig (wie in deiner Saldo-SQL):
                decimal einnahmen = 0m, ausgaben = 0m;

                if (t.NachKontoId.HasValue)
                {
                    // Bank -> Budgetkonto
                    if (_incomeAccounts.Contains(t.NachKontoId.Value))
                        einnahmen = t.Betrag;      // Ertragskonto → Zugang zur Bank
                    else
                        ausgaben = t.Betrag;       // Aufwand/sonst → Abgang von Bank
                }
                else
                {
                    // Bank-only (kein NachKontoId)
                    if (t.VonKontoId.HasValue || t.AdresseId.HasValue)
                        einnahmen = t.Betrag;      // Budget→Bank oder Adresse→Bank
                }

                Rows.Add(new Row
                {
                    Id = t.Id,
                    Datum = t.Datum,
                    Konto = konto,
                    Einnahmen = einnahmen,
                    Ausgaben = ausgaben,
                    AdresseName = t.AdresseName,
                    Notiz = t.Notiz
                });
            }

            OnPropertyChanged(nameof(SummaryText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public sealed class Row
        {
            public int Id { get; set; }
            public DateTime Datum { get; set; }
            public string Konto { get; set; } = "";
            public decimal Einnahmen { get; set; }
            public decimal Ausgaben { get; set; }
            public string? AdresseName { get; set; }
            public string? Notiz { get; set; }
        }
    }
}
