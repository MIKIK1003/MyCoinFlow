using System;
using System.Collections.Generic;
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
    public sealed class KontoTransaktionenViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly int _kontoId;
        private readonly bool _isIncomeAccount;  // ← Konto-Art: true = Einnahmenkonto
        public string Titel { get; }

        // Filter
        public DateTime? FilterVon { get => _filterVon; set { _filterVon = value; OnPropertyChanged(); UpdateSummary(); } }
        private DateTime? _filterVon;

        public DateTime? FilterBis { get => _filterBis; set { _filterBis = value; OnPropertyChanged(); UpdateSummary(); } }
        private DateTime? _filterBis;

        public decimal? FilterMinBetrag { get => _filterMinBetrag; set { _filterMinBetrag = value; OnPropertyChanged(); } }
        private decimal? _filterMinBetrag;

        public decimal? FilterMaxBetrag { get => _filterMaxBetrag; set { _filterMaxBetrag = value; OnPropertyChanged(); } }
        private decimal? _filterMaxBetrag;

        public int? FilterAdresseId { get => _filterAdresseId; set { _filterAdresseId = value; OnPropertyChanged(); } }
        private int? _filterAdresseId;

        public int? FilterGeldinstitutId { get => _filterGeldinstitutId; set { _filterGeldinstitutId = value; OnPropertyChanged(); } }
        private int? _filterGeldinstitutId;

        // Auswahllisten
        public ObservableCollection<Adresse> Adressen { get; } = new();
        public ObservableCollection<Geldinstitut> Geldinstitute { get; } = new();

        // Rows
        public ObservableCollection<Row> Rows { get; } = new();

        // Summen (für Header/Druck)
        public decimal SumEinnahmen { get => _sumEinnahmen; private set { _sumEinnahmen = value; OnPropertyChanged(); UpdateSummary(); } }
        private decimal _sumEinnahmen;

        public decimal SumAusgaben { get => _sumAusgaben; private set { _sumAusgaben = value; OnPropertyChanged(); UpdateSummary(); } }
        private decimal _sumAusgaben;

        /// <summary>Saldo = SumEinnahmen − SumAusgaben (Ist im Zeitraum)</summary>
        public decimal Saldo { get => _saldo; private set { _saldo = value; OnPropertyChanged(); UpdateSummary(); } }
        private decimal _saldo;

        /// <summary>Budgetwert im Zeitraum (0 wenn kein Treffer)</summary>
        public decimal Budget { get => _budget; private set { _budget = value; OnPropertyChanged(); UpdateSummary(); } }
        private decimal _budget;

        /// <summary>Delta:
        ///  Ausgabenkonto: Budget − Istverbrauch = Budget + Saldo
        ///  Einnahmenkonto: Budget − IstEinnahmen = Budget − Saldo
        /// </summary>
        public decimal Delta { get => _delta; private set { _delta = value; OnPropertyChanged(); UpdateSummary(); } }
        private decimal _delta;

        public ICommand ApplyFilterCommand { get; }
        public ICommand ResetFilterCommand { get; }

        public string SummaryText
            => $"Einnahmen {SumEinnahmen:N2}   |   Ausgaben {SumAusgaben:N2}   |   Saldo {Saldo:N2}   |   Budget {Budget:N2}   |   Δ {Delta:N2}";

        public KontoTransaktionenViewModel(int kontoId, string kontoName)
        {
            _kontoId = kontoId;
            Titel = $"Transaktionen – {kontoName}";

            // Konto-Art ermitteln (Einnahmenkonto ja/nein)
            _isIncomeAccount = DetectIncomeAccount(kontoId);

            ApplyFilterCommand = new RelayCommand(_ => Load(), _ => true);
            ResetFilterCommand = new RelayCommand(_ =>
            {
                FilterVon = null; FilterBis = null;
                FilterMinBetrag = null; FilterMaxBetrag = null;
                FilterAdresseId = null; FilterGeldinstitutId = null;

                // NEU: Reset auf aktiven Budgetzeitraum (wie Default)
                PrefillDateRangeFromActiveBudget();

                Load();
            });

            foreach (var a in _db.LadeAdressen().OrderBy(a => a.Name)) Adressen.Add(a);
            foreach (var gi in _db.LadeGeldinstitute().OrderBy(g => g.Name)) Geldinstitute.Add(gi);

            // NEU: Default = aktiver Budgetzeitraum
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
                        // NEU: Default = aktiver Budgetzeitraum
                        FilterVon = bz.Startdatum.Date;
                        FilterBis = bz.Enddatum.Date;
                        return;
                    }
                }

                // Falls kein aktiver Zeitraum existiert:
                // Filter bewusst leer lassen (zeigt alle Daten)
                FilterVon = null;
                FilterBis = null;
            }
            catch
            {
                // defensiv: Filter leer lassen
                FilterVon = null;
                FilterBis = null;
            }
        }


        private bool DetectIncomeAccount(int kontoId)
        {
            // gleiche Heuristik wie in deiner Banksaldo-SQL:
            // Kontonummer 3xxx/7xxx oder Text enthält "Einnahm/Ertr/Income/Revenue"
            var kp = _db.LadeKontenplan().FirstOrDefault(x => x.Id == kontoId);
            if (kp == null) return false;

            bool ByNumber = (kp.Kontonummer >= 3000 && kp.Kontonummer <= 3999)
                         || (kp.Kontonummer >= 7000 && kp.Kontonummer <= 7999);

            static bool HasIncomeWord(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var u = s.ToUpperInvariant();
                return u.Contains("EINNAHM") || u.Contains("ERTR") || u.Contains("INCOME") || u.Contains("REVENUE");
            }
            bool ByText = HasIncomeWord(kp.Art) || HasIncomeWord(kp.Gruppe) || HasIncomeWord(kp.Untergruppe) || HasIncomeWord(kp.Detail);

            return ByNumber || ByText;
        }

        private void Load()
        {
            Rows.Clear();

            var list = _db.LadeTransaktionenByKonto(
                _kontoId, FilterVon, FilterBis, FilterMinBetrag, FilterMaxBetrag, FilterAdresseId, FilterGeldinstitutId);

            decimal einSum = 0m, ausSum = 0m;

            // Adress-Cache, um DB-Calls zu minimieren
            var adrCache = new Dictionary<int, Adresse>();
            Adresse? LoadAdresse(int id)
            {
                if (adrCache.TryGetValue(id, out var a)) return a;
                try { a = _db.HoleAdresse(id); } catch { a = null; }
                if (a != null) adrCache[id] = a;
                return a;
            }

            foreach (var t in list.OrderByDescending(x => x.Datum).ThenByDescending(x => x.Id))
            {
                decimal ein = 0m, aus = 0m;

                // (1) Gutschrift/Storno: Von = Konto, Nach = NULL  => Einnahme
                if (t.VonKontoId == _kontoId && t.NachKontoId == null)
                {
                    ein = t.Betrag;
                }
                // (2) BANK -> KONTO: Von = NULL, Nach = Konto
                else if (t.VonKontoId == null && t.NachKontoId == _kontoId)
                {
                    // Ausnahme: "Standard-Einnahme"-Adresse -> Einnahme
                    bool istStandardEinnahme = false;
                    if (t.AdresseId.HasValue)
                    {
                        var adr = LoadAdresse(t.AdresseId.Value);
                        if (adr?.StandardEinnahmenKontoId == _kontoId || adr?.IstBudgetiert == true)
                            istStandardEinnahme = true;
                    }

                    if (istStandardEinnahme)
                    {
                        ein = t.Betrag; // unverändert: explizit Einnahme
                    }
                    else
                    {
                        // NEU: Konto-Detail soll wie Adressen-Detail klassifizieren:
                        // Einnahmenkonto => Einnahme, sonst Ausgabe.
                        if (_isIncomeAccount)
                            ein = t.Betrag; // NEU
                        else
                            aus = t.Betrag; // unverändert
                    }
                }
                // (3) Rest inkl. Konto->Konto (KK-Detailverteilung Durchlauf -> Budget)
                else
                {
                    bool istAusgabe = _db.IstAusgabeFuerKonto(_kontoId, t); // enthält KK-Sonderfall
                    if (istAusgabe) aus = t.Betrag; else ein = t.Betrag;
                }

                einSum += ein;
                ausSum += aus;

                Rows.Add(new Row
                {
                    Id = t.Id,
                    Datum = t.Datum,
                    GeldinstitutName = t.BankName,
                    Einnahmen = ein,
                    Ausgaben = aus,
                    AdresseName = t.AdresseName,
                    Notiz = t.Notiz,
                    BudgetDelta = 0m
                });
            }

            SumEinnahmen = einSum;
            SumAusgaben = ausSum;
            Saldo = einSum - ausSum;

            // Budget (aktive/überlappende Zeiträume)
            Budget = _db.LadeBudgetSummeForKonto(_kontoId, FilterVon, FilterBis);

            // Delta:
            //  - Einnahmenkonto: Delta = Budget − IstEinnahmen = Budget − Saldo
            //  - Ausgabenkonto:  Delta = Budget − Istverbrauch = Budget + Saldo
            Delta = _isIncomeAccount ? (Budget - Saldo) : (Budget + Saldo);
        }




        private void UpdateSummary() => OnPropertyChanged(nameof(SummaryText));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public sealed class Row
        {
            public int Id { get; set; }
            public DateTime Datum { get; set; }
            public string? GeldinstitutName { get; set; }
            public decimal Einnahmen { get; set; }
            public decimal Ausgaben { get; set; }
            public string? AdresseName { get; set; }
            public string? Notiz { get; set; }
            public decimal BudgetDelta { get; set; }
        }
    }
}
