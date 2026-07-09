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

        private DateTime? _activeStart;
        private DateTime? _activeEnd;
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

        // --- Summen (berechnet aus Rows; kein Extra-Code im Load nötig) ---
        public decimal SummeEinnahmen => Rows.Sum(r => r.Einnahmen);
        public decimal SummeAusgaben => Rows.Sum(r => r.Ausgaben);
        public decimal Saldo => SummeEinnahmen - SummeAusgaben;

        // Komfort: formatiert für die Anzeige
        public string SummeEinnahmenText => SummeEinnahmen.ToString("N2");
        public string SummeAusgabenText => SummeAusgaben.ToString("N2");
        public string SaldoText => Saldo.ToString("N2");




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

            var activeId = _db.HoleAktivenBudgetzeitraumId();
            if (activeId.HasValue)
            {
                var bz = _db.HoleBudgetzeitraum(activeId.Value);
                if (bz != null)
                {
                    _activeStart = bz.Startdatum.Date;
                    _activeEnd = bz.Enddatum.Date;

                    if (!FilterVon.HasValue)
                        FilterVon = _activeStart;

                    if (!FilterBis.HasValue)
                        FilterBis = _activeEnd;
                }
            }

            Load();
        }

        private static string StripClassification(string anzeige)
        {
            // "1234 Detail [Art/Gruppe/UG]" -> "1234 Detail"
            if (string.IsNullOrWhiteSpace(anzeige)) return "";
            var idx = anzeige.IndexOf(" [", StringComparison.Ordinal);
            return idx >= 0 ? anzeige[..idx].Trim() : anzeige.Trim();
        }

        // GeldinstitutTransaktionenViewModel.cs
        // — KOMPLETTE METHODE ERSETZEN —
        private System.Collections.Generic.HashSet<int> BuildIncomeAccountSet()
        {
            var set = new System.Collections.Generic.HashSet<int>();

            // Alle Konten laden und pro Konto die zentrale Regel aus DatabaseService nutzen.
            // Damit gelten deine Nummernkreise (Admin -> Nummernkreise) 1:1,
            // ohne harte Bereiche wie 3000–3999 im Code.
            foreach (var k in _db.LadeKontenplan())
            {
                if (_db.IstEinnahmenKonto(k.Id))
                    set.Add(k.Id);
            }
            return set;
        }



        private void Load()
        {
            Rows.Clear();

            var list = _db.LadeTransaktionenByGeldinstitut(
                _giId, FilterVon, FilterBis, FilterMinBetrag, FilterMaxBetrag, FilterAdresseId, FilterKontoId);

            foreach (var t in list.OrderByDescending(t => t.Datum).ThenByDescending(t => t.Id))
            {
                // DEFENSIV: KK-Detailverteilungen gehören NICHT in die Bankansicht.
                // Falls so eine Zeile irrtümlich doch eine GeldinstitutId hätte: einfach überspringen.
                if (string.Equals(t.ImportQuelle, "KreditkartenExcel", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Kontoanzeige: bevorzugt VON, dann NACH, sonst Bankname – ohne Klassifikationsanhang
                string konto =
                      (t.VonKontoId.HasValue && _kontoLabel.TryGetValue(t.VonKontoId.Value, out var vLbl)) ? vLbl
                    : (t.NachKontoId.HasValue && _kontoLabel.TryGetValue(t.NachKontoId.Value, out var nLbl)) ? nLbl
                    : (t.BankName ?? "Bank");

                // Einnahmen/Ausgaben bankseitig (wie deine Saldo-Sicht):
                decimal einnahmen = 0m, ausgaben = 0m;

                if (t.NachKontoId.HasValue)
                {
                    // Bank -> Konto
                    if (_incomeAccounts.Contains(t.NachKontoId.Value))
                        einnahmen = t.Betrag;   // Ertragskonto -> Zugang zur Bank
                    else
                        ausgaben = t.Betrag;    // Aufwand/sonst -> Abgang von Bank
                }
                else
                {
                    // Bank-only (kein NachKontoId)
                    if (t.VonKontoId.HasValue || t.AdresseId.HasValue)
                        einnahmen = t.Betrag;   // Budget->Bank oder Adresse->Bank
                }

                Rows.Add(new Row
                {
                    Id = t.Id,
                    Datum = t.Datum,
                    BudgetDatum = t.BudgetDatum,

                    HasBudgetDatumOverride =
    t.BudgetDatum.HasValue
    && _activeStart.HasValue
    && _activeEnd.HasValue
    && (t.Datum.Date < _activeStart.Value
        || t.Datum.Date > _activeEnd.Value),

                    BudgetDatumTooltip =
    t.BudgetDatum.HasValue
    ? $"Budgetdatum: {t.BudgetDatum:dd.MM.yyyy}\nBankdatum: {t.Datum:dd.MM.yyyy}"
    : null,
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
            public DateTime? BudgetDatum { get; set; }
            public bool HasBudgetDatumOverride { get; set; }
            public string? BudgetDatumTooltip { get; set; }
            public string Konto { get; set; } = "";
            public decimal Einnahmen { get; set; }
            public decimal Ausgaben { get; set; }
            public string? AdresseName { get; set; }
            public string? Notiz { get; set; }
        }
    }
}
