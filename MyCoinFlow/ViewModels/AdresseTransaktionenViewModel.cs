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
    /// Transaktionen zu einer Adresse mit Filtern, Summenzeile und getrennten Spalten Einnahmen/Ausgaben.
    /// </summary>
    public sealed class AdresseTransaktionenViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly int _adrId;
        private DateTime? _activeStart;
        private DateTime? _activeEnd;

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

        // Auswahllisten
        public ObservableCollection<KontoLookup> KontenLookup { get; } = new();
        public ObservableCollection<Geldinstitut> Geldinstitute { get; } = new();

        // Grid-Zeilen
        public ObservableCollection<Row> Rows { get; } = new();

        // Commands
        public ICommand ApplyFilterCommand { get; }
        public ICommand ResetFilterCommand { get; }

        // Zusammenfassung (alt – bleibt, falls irgendwo genutzt)
        public string SummaryText
        {
            get
            {
                var ein = Rows.Sum(r => r.Einnahmen);
                var aus = Rows.Sum(r => r.Ausgaben);
                var saldo = ein - aus;
                return $"Einnahmen {ein:N2}{Environment.NewLine}" +
                       $"Ausgaben {aus:N2}{Environment.NewLine}" +
                       $"Saldo {saldo:N2}";
            }
        }

        // NEU: Summen einzeln (für links/rechts Layout)
        public decimal SummeEinnahmen => Rows.Sum(r => r.Einnahmen);
        public decimal SummeAusgaben => Rows.Sum(r => r.Ausgaben);
        public decimal Saldo => SummeEinnahmen - SummeAusgaben;

        public string SummeEinnahmenText => SummeEinnahmen.ToString("N2");
        public string SummeAusgabenText => SummeAusgaben.ToString("N2");
        public string SaldoText => Saldo.ToString("N2");

        // Kontoanzeige ohne Klassifikation
        private System.Collections.Generic.Dictionary<int, string> _kontoLabel = new();

        // Ertragskonten-Set
        private readonly System.Collections.Generic.HashSet<int> _incomeAccounts;

        public AdresseTransaktionenViewModel(int adresseId, string adresseName)
        {
            _adrId = adresseId;
            Titel = $"Transaktionen – {adresseName} (Adresse)";

            ApplyFilterCommand = new RelayCommand(_ => Load(), _ => true);

            ResetFilterCommand = new RelayCommand(_ =>
            {
                FilterVon = null;
                FilterBis = null;

                FilterMinBetrag = null;
                FilterMaxBetrag = null;

                FilterKontoId = null;
                FilterGeldinstitutId = null;

                // Reset auf aktiven Budgetzeitraum
                PrefillDateRangeFromActiveBudget();

                Load();
            });

            foreach (var k in _db.LadeKontoLookup()) KontenLookup.Add(k);
            _kontoLabel = KontenLookup.ToDictionary(k => k.Id, k =>
            {
                var anzeige = k.Anzeige ?? "";
                var idx = anzeige.IndexOf(" [", StringComparison.Ordinal);
                return idx >= 0 ? anzeige[..idx].Trim() : anzeige.Trim();
            });

            foreach (var gi in _db.LadeGeldinstitute()) Geldinstitute.Add(gi);

            _incomeAccounts = BuildIncomeAccountSet();

            PrefillDateRangeFromActiveBudget();

            Load();
        }

        private void PrefillDateRangeFromActiveBudget()
        {
            try
            {
                var activeId = _db.HoleAktivenBudgetzeitraumId();

                if (activeId.HasValue)
                {
                    var bz = _db.HoleBudgetzeitraum(activeId.Value);

                    if (bz != null)
                    {
                        FilterVon = bz.Startdatum.Date;
                        FilterBis = bz.Enddatum.Date;

                        _activeStart = bz.Startdatum.Date;
                        _activeEnd = bz.Enddatum.Date;

                        return;
                    }
                }

                FilterVon = null;
                FilterBis = null;

                _activeStart = null;
                _activeEnd = null;
            }
            catch
            {
                FilterVon = null;
                FilterBis = null;

                _activeStart = null;
                _activeEnd = null;
            }
        }

        private System.Collections.Generic.HashSet<int> BuildIncomeAccountSet()
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (var k in _db.LadeKontenplan())
            {
                bool isIncome =
                       (k.Kontonummer >= 3000 && k.Kontonummer <= 3999)
                    || (k.Kontonummer >= 7000 && k.Kontonummer <= 7999)
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
                string konto =
                      (t.VonKontoId.HasValue && _kontoLabel.TryGetValue(t.VonKontoId.Value, out var vLbl)) ? vLbl
                    : (t.NachKontoId.HasValue && _kontoLabel.TryGetValue(t.NachKontoId.Value, out var nLbl)) ? nLbl
                    : "Konto";

                decimal einnahmen = 0m, ausgaben = 0m;

                if (t.NachKontoId.HasValue)
                {
                    if (_incomeAccounts.Contains(t.NachKontoId.Value))
                        einnahmen = t.Betrag;
                    else
                        ausgaben = t.Betrag;
                }
                else
                {
                    if (t.VonKontoId.HasValue || t.GeldinstitutId.HasValue)
                        einnahmen = t.Betrag;
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
                    Einnahmen = einnahmen > 0 ? einnahmen : 0m,
                    Ausgaben = ausgaben > 0 ? ausgaben : 0m,
                    GeldinstitutName = t.BankName,
                    Notiz = t.Notiz
                });
            }

            // Summen aktualisieren
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(SummeEinnahmen));
            OnPropertyChanged(nameof(SummeAusgaben));
            OnPropertyChanged(nameof(Saldo));
            OnPropertyChanged(nameof(SummeEinnahmenText));
            OnPropertyChanged(nameof(SummeAusgabenText));
            OnPropertyChanged(nameof(SaldoText));
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

            public string? GeldinstitutName { get; set; }

            public string? Notiz { get; set; }
        }
    }
}
