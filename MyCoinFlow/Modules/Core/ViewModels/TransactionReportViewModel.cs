using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public sealed class TransactionReportViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();
        private readonly TransactionReportCalculator _calculator = new();
        private readonly List<NumberRangeRule> _numberRangeRules = new();

        public ObservableCollection<Budgetzeitraum> Budgetzeitraeume { get; } = new();
        public ObservableCollection<TransactionReportChoice<TransactionReportMode>> Berichtsarten { get; } = new();
        public ObservableCollection<TransactionReportChoice<TransactionReportGrouping>> Gruppierungen { get; } = new();
        public ObservableCollection<TransactionReportNumberRangeSelection> Nummernkreise { get; } = new();
        public ObservableCollection<TransactionReportAccountSelection> Konten { get; } = new();
        public ObservableCollection<TransactionReportRow> Ergebniszeilen { get; } = new();
        public ObservableCollection<TransactionReportRow> GroessteAbweichungen { get; } = new();
        public ObservableCollection<TransactionReportSpotlightRow> GroessteAusgaben { get; } = new();
        public ObservableCollection<TransactionReportSpotlightRow> GroessteEinnahmen { get; } = new();

        public ICommand AlleKontenCommand { get; }
        public ICommand KeineKontenCommand { get; }
        public ICommand NurBudgetkontenCommand { get; }
        public ICommand NummernkreiseAnwendenCommand { get; }
        public ICommand AuswertenCommand { get; }

        private string _berichtstitel = "Transaktionsbericht";
        public string Berichtstitel
        {
            get => _berichtstitel;
            set { _berichtstitel = value; OnPropertyChanged(); }
        }

        private Budgetzeitraum? _ausgewaehlterBudgetzeitraum;
        public Budgetzeitraum? AusgewaehlterBudgetzeitraum
        {
            get => _ausgewaehlterBudgetzeitraum;
            set
            {
                if (_ausgewaehlterBudgetzeitraum == value) return;
                _ausgewaehlterBudgetzeitraum = value;
                OnPropertyChanged();
                ZeitraumVorbelegen();
                KontenLaden();
                ErgebnisLeeren();
            }
        }

        private TransactionReportChoice<TransactionReportMode>? _ausgewaehlteBerichtsart;
        public TransactionReportChoice<TransactionReportMode>? AusgewaehlteBerichtsart
        {
            get => _ausgewaehlteBerichtsart;
            set { _ausgewaehlteBerichtsart = value; OnPropertyChanged(); ErgebnisLeeren(); }
        }

        private TransactionReportChoice<TransactionReportGrouping>? _ausgewaehlteGruppierung;
        public TransactionReportChoice<TransactionReportGrouping>? AusgewaehlteGruppierung
        {
            get => _ausgewaehlteGruppierung;
            set
            {
                _ausgewaehlteGruppierung = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(KontoSpaltenTitel));
                ErgebnisLeeren();
            }
        }

        public string KontoSpaltenTitel =>
            AusgewaehlteGruppierung?.Value == TransactionReportGrouping.Einzelkonto
                ? "Konto"
                : "Konto ab";

        private DateTime? _auswertungVon;
        public DateTime? AuswertungVon
        {
            get => _auswertungVon;
            set { _auswertungVon = value; OnPropertyChanged(); ErgebnisLeeren(); }
        }

        private DateTime? _auswertungBis;
        public DateTime? AuswertungBis
        {
            get => _auswertungBis;
            set { _auswertungBis = value; OnPropertyChanged(); ErgebnisLeeren(); }
        }

        private string _statusText = "Bericht konfigurieren und Auswerten wählen.";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        private string _ausgabenZusammenfassung = "Ausgaben: noch keine Auswertung";
        public string AusgabenZusammenfassung
        {
            get => _ausgabenZusammenfassung;
            private set { _ausgabenZusammenfassung = value; OnPropertyChanged(); }
        }

        private string _einnahmenZusammenfassung = "Einnahmen: noch keine Auswertung";
        public string EinnahmenZusammenfassung
        {
            get => _einnahmenZusammenfassung;
            private set { _einnahmenZusammenfassung = value; OnPropertyChanged(); }
        }

        private string _nettoZusammenfassung = "Netto: noch keine Auswertung";
        public string NettoZusammenfassung
        {
            get => _nettoZusammenfassung;
            private set { _nettoZusammenfassung = value; OnPropertyChanged(); }
        }

        public int AusgewaehlteKontenAnzahl => Konten.Count(k => k.IsSelected);
        public bool HatErgebnis => CurrentResult != null;
        public bool KannBudgetAnpassen => CurrentResult != null
                                          && CurrentResult.Optionen.Modus != TransactionReportMode.NurBudget
                                          && CurrentResult.EinzelkontoZeilen.Any(z => z.HochrechnungJahr.HasValue);
        public TransactionReportResult? CurrentResult { get; private set; }

        private string _spotlightBasis = "Noch keine Auswertung";
        public string SpotlightBasis
        {
            get => _spotlightBasis;
            private set { _spotlightBasis = value; OnPropertyChanged(); }
        }

        private string _spotlightZusammenfassung = "Top-5-Anteile werden nach der Auswertung angezeigt.";
        public string SpotlightZusammenfassung
        {
            get => _spotlightZusammenfassung;
            private set { _spotlightZusammenfassung = value; OnPropertyChanged(); }
        }

        public TransactionReportViewModel()
        {
            AlleKontenCommand = new RelayCommand(_ => AuswahlSetzen(_ => true));
            KeineKontenCommand = new RelayCommand(_ => AuswahlSetzen(_ => false));
            NurBudgetkontenCommand = new RelayCommand(_ => AuswahlSetzen(k => k.Jahresbudget.HasValue));
            NummernkreiseAnwendenCommand = new RelayCommand(_ => NummernkreiseAnwenden());
            AuswertenCommand = new RelayCommand(_ => Auswerten());

            Berichtsarten.Add(new TransactionReportChoice<TransactionReportMode>(
                "Soll/Ist mit Jahreshochrechnung", TransactionReportMode.SollIstMitHochrechnung));
            Berichtsarten.Add(new TransactionReportChoice<TransactionReportMode>(
                "Nur Ist mit Jahreshochrechnung", TransactionReportMode.IstMitHochrechnung));
            Berichtsarten.Add(new TransactionReportChoice<TransactionReportMode>(
                "Nur Jahresbudget", TransactionReportMode.NurBudget));
            AusgewaehlteBerichtsart = Berichtsarten[0];

            Gruppierungen.Add(new TransactionReportChoice<TransactionReportGrouping>(
                "Einzelkonto", TransactionReportGrouping.Einzelkonto));
            Gruppierungen.Add(new TransactionReportChoice<TransactionReportGrouping>(
                "Art", TransactionReportGrouping.Art));
            Gruppierungen.Add(new TransactionReportChoice<TransactionReportGrouping>(
                "Gruppe", TransactionReportGrouping.Gruppe));
            Gruppierungen.Add(new TransactionReportChoice<TransactionReportGrouping>(
                "Untergruppe", TransactionReportGrouping.Untergruppe));
            AusgewaehlteGruppierung = Gruppierungen[3];

            GrundlagenLaden();
        }

        private void GrundlagenLaden()
        {
            try
            {
                _numberRangeRules.Clear();
                _numberRangeRules.AddRange(_db.LadeNummernRegeln());

                Nummernkreise.Clear();
                foreach (var regel in _numberRangeRules.OrderBy(r => r.RangeStart))
                {
                    var label = string.IsNullOrWhiteSpace(regel.Bezeichnung)
                        ? $"{regel.Richtung} {regel.RangeStart}-{regel.RangeEnd}"
                        : regel.Bezeichnung!;

                    Nummernkreise.Add(new TransactionReportNumberRangeSelection
                    {
                        Start = regel.RangeStart,
                        End = regel.RangeEnd,
                        Anzeige = $"{label} ({regel.RangeStart}-{regel.RangeEnd})"
                    });
                }

                Budgetzeitraeume.Clear();
                foreach (var zeitraum in _db.LadeBudgetzeitraeume()
                             .OrderByDescending(z => z.IstAktiv)
                             .ThenByDescending(z => z.Startdatum))
                {
                    Budgetzeitraeume.Add(zeitraum);
                }

                AusgewaehlterBudgetzeitraum = Budgetzeitraeume.FirstOrDefault(z => z.IstAktiv)
                                              ?? Budgetzeitraeume.FirstOrDefault();

                if (AusgewaehlterBudgetzeitraum == null)
                    StatusText = "Es ist kein Budgetzeitraum vorhanden. Bitte zuerst unter Budget einen Zeitraum anlegen.";
            }
            catch (Exception ex)
            {
                StatusText = "Berichtsgrundlagen konnten nicht geladen werden: " + ex.Message;
            }
        }

        private void ZeitraumVorbelegen()
        {
            var zeitraum = AusgewaehlterBudgetzeitraum;
            if (zeitraum == null)
            {
                AuswertungVon = null;
                AuswertungBis = null;
                return;
            }

            AuswertungVon = zeitraum.Startdatum.Date;
            AuswertungBis = DateTime.Today >= zeitraum.Startdatum.Date && DateTime.Today <= zeitraum.Enddatum.Date
                ? DateTime.Today
                : zeitraum.Enddatum.Date;
        }

        private void KontenLaden()
        {
            Konten.Clear();
            if (AusgewaehlterBudgetzeitraum == null)
            {
                OnPropertyChanged(nameof(AusgewaehlteKontenAnzahl));
                return;
            }

            try
            {
                var budgetKonten = _db.LadeBudgetKontenFuerZeitraum(AusgewaehlterBudgetzeitraum.Id);
                foreach (var konto in budgetKonten.OrderBy(k => k.Kontonummer))
                {
                    var regel = PassendeRegel(konto.Kontonummer);
                    var richtung = ErmittleRichtung(konto, regel);

                    Konten.Add(new TransactionReportAccountSelection(AuswahlGeaendert)
                    {
                        KontoId = konto.KontoId,
                        Kontonummer = konto.Kontonummer,
                        Art = konto.Art,
                        Gruppe = konto.Gruppe,
                        Untergruppe = konto.Untergruppe,
                        Detail = konto.Detail,
                        Jahresbudget = konto.Budgetwert,
                        Richtung = richtung,
                        IsSelected = IstStandardauswahl(regel, richtung)
                    });
                }

                OnPropertyChanged(nameof(AusgewaehlteKontenAnzahl));
                StatusText = $"{Konten.Count} Konten geladen, {AusgewaehlteKontenAnzahl} vorausgewählt.";
            }
            catch (Exception ex)
            {
                StatusText = "Konten konnten nicht geladen werden: " + ex.Message;
            }
        }

        private NumberRangeRule? PassendeRegel(int kontonummer)
        {
            return _numberRangeRules
                .Where(r => kontonummer >= r.RangeStart && kontonummer <= r.RangeEnd)
                .OrderBy(r => r.RangeEnd - r.RangeStart)
                .FirstOrDefault();
        }

        private static TransactionReportDirection ErmittleRichtung(
            BudgetKontoRow konto,
            NumberRangeRule? regel)
        {
            if (regel != null)
            {
                if (string.Equals(regel.Richtung, "Einnahme", StringComparison.OrdinalIgnoreCase))
                    return TransactionReportDirection.Einnahme;
                if (string.Equals(regel.Richtung, "Neutral", StringComparison.OrdinalIgnoreCase))
                    return TransactionReportDirection.Neutral;
                return TransactionReportDirection.Ausgabe;
            }

            var text = $"{konto.Art} {konto.Gruppe} {konto.Untergruppe} {konto.Detail}".ToUpperInvariant();
            var istEinnahme = (konto.Kontonummer >= 3000 && konto.Kontonummer <= 3999)
                               || (konto.Kontonummer >= 7000 && konto.Kontonummer <= 7999)
                               || text.Contains("EINNAHM")
                               || text.Contains("ERTRAG")
                               || text.Contains("ERLÖS")
                               || text.Contains("ERLOES")
                               || text.Contains("INCOME")
                               || text.Contains("REVENUE");

            return istEinnahme ? TransactionReportDirection.Einnahme : TransactionReportDirection.Ausgabe;
        }

        private static bool IstStandardauswahl(NumberRangeRule? regel, TransactionReportDirection richtung)
        {
            if (richtung == TransactionReportDirection.Neutral)
                return false;

            var label = (regel?.Bezeichnung ?? "").ToLowerInvariant();
            return !label.Contains("invest")
                   && !label.Contains("amort")
                   && !label.Contains("durchlauf");
        }

        private void AuswahlSetzen(Func<TransactionReportAccountSelection, bool> auswahl)
        {
            foreach (var konto in Konten)
                konto.IsSelected = auswahl(konto);

            AuswahlGeaendert();
            ErgebnisLeeren();
        }

        private void NummernkreiseAnwenden()
        {
            var kreise = Nummernkreise.Where(k => k.IsSelected).ToList();
            if (kreise.Count == 0)
            {
                StatusText = "Bitte mindestens einen Nummernkreis markieren.";
                return;
            }

            foreach (var konto in Konten)
                konto.IsSelected = kreise.Any(k => konto.Kontonummer >= k.Start && konto.Kontonummer <= k.End);

            AuswahlGeaendert();
            ErgebnisLeeren();
            StatusText = $"{AusgewaehlteKontenAnzahl} Konten über Nummernkreise ausgewählt.";
        }

        private void AuswahlGeaendert()
        {
            OnPropertyChanged(nameof(AusgewaehlteKontenAnzahl));
        }

        private void Auswerten()
        {
            try
            {
                var zeitraum = AusgewaehlterBudgetzeitraum
                    ?? throw new InvalidOperationException("Bitte einen Budgetzeitraum wählen.");
                var von = AuswertungVon?.Date
                    ?? throw new InvalidOperationException("Bitte ein Von-Datum wählen.");
                var bis = AuswertungBis?.Date
                    ?? throw new InvalidOperationException("Bitte ein Bis-Datum wählen.");

                if (bis < von)
                    throw new InvalidOperationException("Das Bis-Datum darf nicht vor dem Von-Datum liegen.");
                if (von < zeitraum.Startdatum.Date || bis > zeitraum.Enddatum.Date)
                    throw new InvalidOperationException("Der Auswertungszeitraum muss innerhalb des gewählten Budgetzeitraums liegen.");

                var ausgewaehlteKonten = Konten.Where(k => k.IsSelected).ToList();
                if (ausgewaehlteKonten.Count == 0)
                    throw new InvalidOperationException("Bitte mindestens ein Konto auswählen.");

                var modus = AusgewaehlteBerichtsart?.Value
                    ?? TransactionReportMode.SollIstMitHochrechnung;
                var gruppierung = AusgewaehlteGruppierung?.Value
                    ?? TransactionReportGrouping.Untergruppe;

                var transaktionen = modus == TransactionReportMode.NurBudget
                    ? new List<Transaktion>()
                    : _db.LadeTransaktionenFuerBericht(
                        ausgewaehlteKonten.Select(k => k.KontoId).ToArray(), von, bis);

                var optionen = new TransactionReportOptions
                {
                    Titel = string.IsNullOrWhiteSpace(Berichtstitel) ? "Transaktionsbericht" : Berichtstitel.Trim(),
                    BudgetzeitraumBezeichnung = zeitraum.Bezeichnung,
                    BudgetVon = zeitraum.Startdatum.Date,
                    BudgetBis = zeitraum.Enddatum.Date,
                    AuswertungVon = von,
                    AuswertungBis = bis,
                    Modus = modus,
                    Gruppierung = gruppierung
                };

                CurrentResult = _calculator.Berechnen(
                    optionen,
                    ausgewaehlteKonten.Select(k => k.ToModel()).ToArray(),
                    transaktionen);

                Ergebniszeilen.Clear();
                foreach (var zeile in CurrentResult.Zeilen)
                    Ergebniszeilen.Add(zeile);

                GroessteAbweichungen.Clear();
                foreach (var zeile in CurrentResult.GroessteAbweichungen)
                {
                    GroessteAbweichungen.Add(zeile);
                }

                GroessteAusgaben.Clear();
                foreach (var zeile in CurrentResult.GroessteAusgaben)
                    GroessteAusgaben.Add(zeile);

                GroessteEinnahmen.Clear();
                foreach (var zeile in CurrentResult.GroessteEinnahmen)
                    GroessteEinnahmen.Add(zeile);

                SpotlightBasis = modus == TransactionReportMode.NurBudget
                    ? "Basis: Jahresbudget"
                    : $"Rangfolge: Jahreshochrechnung · Ist {von:dd.MM.yyyy}–{bis:dd.MM.yyyy}";
                SpotlightZusammenfassung = ErstelleSpotlightZusammenfassung(CurrentResult);

                ZusammenfassungenAktualisieren(CurrentResult);
                StatusText = $"Bericht erstellt: {CurrentResult.Zeilen.Count} Zeilen, " +
                             $"{CurrentResult.AusgewaehlteKonten} Konten, {transaktionen.Count} Transaktionen. " +
                             $"{CurrentResult.KontenOhneBudget} ausgewählte Konten ohne Budgetwert.";
                OnPropertyChanged(nameof(HatErgebnis));
                OnPropertyChanged(nameof(KannBudgetAnpassen));
            }
            catch (Exception ex)
            {
                StatusText = "Auswertung nicht möglich: " + ex.Message;
            }
        }

        public BudgetProjectionPreviewViewModel ErstelleBudgetanpassungsVorschau()
        {
            var result = CurrentResult
                ?? throw new InvalidOperationException("Bitte zuerst eine Auswertung mit Hochrechnung erstellen.");
            var zeitraum = AusgewaehlterBudgetzeitraum
                ?? throw new InvalidOperationException("Es ist kein Budgetzeitraum ausgewählt.");

            if (result.Optionen.Modus == TransactionReportMode.NurBudget)
                throw new InvalidOperationException("Ein reiner Budgetbericht enthält keine Hochrechnung.");

            var kontenNachId = Konten.ToDictionary(k => k.KontoId);
            var zeilen = result.EinzelkontoZeilen
                .Where(z => z.KontoId.HasValue
                            && z.HochrechnungJahr.HasValue
                            && z.HochrechnungJahr.Value >= 0m
                            && kontenNachId.TryGetValue(z.KontoId.Value, out var konto)
                            && konto.Richtung != TransactionReportDirection.Neutral)
                .Select(z =>
                {
                    var konto = kontenNachId[z.KontoId!.Value];
                    return new BudgetProjectionAdjustmentRow
                    {
                        KontoId = konto.KontoId,
                        Kontonummer = konto.Kontonummer,
                        Bezeichnung = konto.Detail,
                        Richtung = konto.RichtungText,
                        AlterWert = konto.Jahresbudget,
                        Hochrechnung = Math.Round(z.HochrechnungJahr!.Value, 2, MidpointRounding.AwayFromZero),
                        NeuerWert = Math.Round(z.HochrechnungJahr.Value, 2, MidpointRounding.AwayFromZero)
                    };
                })
                .OrderBy(z => z.Kontonummer)
                .ToList();

            if (zeilen.Count == 0)
                throw new InvalidOperationException("Für die ausgewählten Konten sind keine übernehmbaren Hochrechnungen vorhanden.");

            return new BudgetProjectionPreviewViewModel(
                zeitraum.Bezeichnung,
                result.Auswertungstage,
                result.Budgettage,
                zeilen);
        }

        public int BudgetanpassungenUebernehmen(IEnumerable<BudgetProjectionAdjustmentRow> vorschlaege)
        {
            var zeitraum = AusgewaehlterBudgetzeitraum
                ?? throw new InvalidOperationException("Es ist kein Budgetzeitraum ausgewählt.");
            var auswahl = vorschlaege
                .Where(v => v.IsSelected && Math.Abs(v.Differenz) >= 0.01m)
                .ToList();

            if (auswahl.Count == 0)
                throw new InvalidOperationException("Es sind keine geänderten Budgetwerte markiert.");

            _db.AktualisiereBudgetwerteTransaktional(
                zeitraum.Id,
                auswahl.Select(v => new BudgetwertAenderung
                {
                    KontoId = v.KontoId,
                    NeuerWert = v.NeuerWert
                }).ToArray());

            var kontenNachId = Konten.ToDictionary(k => k.KontoId);
            foreach (var aenderung in auswahl)
            {
                if (kontenNachId.TryGetValue(aenderung.KontoId, out var konto))
                    konto.Jahresbudget = aenderung.NeuerWert;
            }

            Auswerten();
            StatusText = $"{auswahl.Count} Budgetwerte im Zeitraum „{zeitraum.Bezeichnung}“ wurden aktualisiert.";
            return auswahl.Count;
        }

        private void ZusammenfassungenAktualisieren(TransactionReportResult result)
        {
            var modus = result.Optionen.Modus;
            if (modus == TransactionReportMode.NurBudget)
            {
                AusgabenZusammenfassung = $"Ausgaben · Jahresbudget {Betrag(result.Ausgaben.BudgetJahr)}";
                EinnahmenZusammenfassung = $"Einnahmen · Jahresbudget {Betrag(result.Einnahmen.BudgetJahr)}";
                NettoZusammenfassung = $"Budgetiertes Jahresergebnis · {Betrag(
                    (result.Einnahmen.BudgetJahr ?? 0m) - (result.Ausgaben.BudgetJahr ?? 0m))}";
                return;
            }

            if (modus == TransactionReportMode.IstMitHochrechnung)
            {
                AusgabenZusammenfassung = $"Ausgaben · Ist {Betrag(result.Ausgaben.IstZeitraum)} · Hochrechnung {Ganzbetrag(result.Ausgaben.HochrechnungJahr)}";
                EinnahmenZusammenfassung = $"Einnahmen · Ist {Betrag(result.Einnahmen.IstZeitraum)} · Hochrechnung {Ganzbetrag(result.Einnahmen.HochrechnungJahr)}";
                NettoZusammenfassung = $"Hochgerechnetes Jahresergebnis · {Ganzbetrag(
                    (result.Einnahmen.HochrechnungJahr ?? 0m) - (result.Ausgaben.HochrechnungJahr ?? 0m))}";
                return;
            }

            AusgabenZusammenfassung =
                $"Ausgaben · Budget {Betrag(result.Ausgaben.BudgetJahr)} · Ist {Betrag(result.Ausgaben.IstZeitraum)} · Hochrechnung {Ganzbetrag(result.Ausgaben.HochrechnungJahr)} · Δ Jahr {Betrag(result.Ausgaben.DeltaJahr)}";
            EinnahmenZusammenfassung =
                $"Einnahmen · Budget {Betrag(result.Einnahmen.BudgetJahr)} · Ist {Betrag(result.Einnahmen.IstZeitraum)} · Hochrechnung {Ganzbetrag(result.Einnahmen.HochrechnungJahr)} · Δ Jahr {Betrag(result.Einnahmen.DeltaJahr)}";
            NettoZusammenfassung =
                $"Hochgerechnetes Jahresergebnis · {Ganzbetrag((result.Einnahmen.HochrechnungJahr ?? 0m) - (result.Ausgaben.HochrechnungJahr ?? 0m))}";
        }

        private void ErgebnisLeeren()
        {
            CurrentResult = null;
            Ergebniszeilen.Clear();
            GroessteAbweichungen.Clear();
            GroessteAusgaben.Clear();
            GroessteEinnahmen.Clear();
            AusgabenZusammenfassung = "Ausgaben: Auswertung aktualisieren";
            EinnahmenZusammenfassung = "Einnahmen: Auswertung aktualisieren";
            NettoZusammenfassung = "Netto: Auswertung aktualisieren";
            SpotlightBasis = "Noch keine Auswertung";
            SpotlightZusammenfassung = "Top-5-Anteile werden nach der Auswertung angezeigt.";
            OnPropertyChanged(nameof(HatErgebnis));
            OnPropertyChanged(nameof(KannBudgetAnpassen));
        }

        private static string ErstelleSpotlightZusammenfassung(TransactionReportResult result)
        {
            var ausgabenAnteil = result.GroessteAusgaben.Sum(z => z.AnteilProzent);
            var einnahmenAnteil = result.GroessteEinnahmen.Sum(z => z.AnteilProzent);
            if (result.Optionen.Modus == TransactionReportMode.NurBudget)
            {
                return $"Top-5-Anteil am Jahresbudget · Ausgaben {ausgabenAnteil:N1} % · " +
                       $"Einnahmen {einnahmenAnteil:N1} % · Budgetabdeckung {result.BudgetabdeckungProzent:N1} %";
            }

            var ausgabenHochrechnung = result.GroessteAusgaben.Sum(z => z.HochrechnungAnteilProzent ?? 0m);
            var einnahmenHochrechnung = result.GroessteEinnahmen.Sum(z => z.HochrechnungAnteilProzent ?? 0m);
            return $"Top-5-Anteil · Ist: Ausgaben {ausgabenAnteil:N1} %, Einnahmen {einnahmenAnteil:N1} % · " +
                   $"Hochrechnung: Ausgaben {ausgabenHochrechnung:N1} %, Einnahmen {einnahmenHochrechnung:N1} % · " +
                   $"Budgetabdeckung {result.BudgetabdeckungProzent:N1} %";
        }

        private static string Betrag(decimal? wert)
        {
            return wert.HasValue
                ? wert.Value.ToString("N2", CultureInfo.GetCultureInfo("de-CH"))
                : "–";
        }

        private static string Ganzbetrag(decimal? wert)
        {
            return wert.HasValue
                ? wert.Value.ToString("N0", CultureInfo.GetCultureInfo("de-CH"))
                : "–";
        }
    }

    public sealed class TransactionReportChoice<T>
    {
        public TransactionReportChoice(string display, T value)
        {
            Display = display;
            Value = value;
        }

        public string Display { get; }
        public T Value { get; }
    }

    public sealed class TransactionReportNumberRangeSelection : BaseViewModel
    {
        public int Start { get; init; }
        public int End { get; init; }
        public string Anzeige { get; init; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }

    public sealed class TransactionReportAccountSelection : BaseViewModel
    {
        private readonly Action _selectionChanged;

        public TransactionReportAccountSelection(Action selectionChanged)
        {
            _selectionChanged = selectionChanged;
        }

        public int KontoId { get; init; }
        public int Kontonummer { get; init; }
        public string Art { get; init; } = "";
        public string Gruppe { get; init; } = "";
        public string Untergruppe { get; init; } = "";
        public string Detail { get; init; } = "";
        private decimal? _jahresbudget;
        public decimal? Jahresbudget
        {
            get => _jahresbudget;
            set { _jahresbudget = value; OnPropertyChanged(); }
        }
        public TransactionReportDirection Richtung { get; init; }
        public string RichtungText => Richtung switch
        {
            TransactionReportDirection.Einnahme => "Einnahme",
            TransactionReportDirection.Ausgabe => "Ausgabe",
            _ => "Neutral"
        };

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                _selectionChanged();
            }
        }

        public TransactionReportAccount ToModel() => new()
        {
            KontoId = KontoId,
            Kontonummer = Kontonummer,
            Art = Art,
            Gruppe = Gruppe,
            Untergruppe = Untergruppe,
            Detail = Detail,
            Jahresbudget = Jahresbudget,
            Richtung = Richtung
        };
    }

    public sealed class BudgetProjectionAdjustmentRow : BaseViewModel
    {
        private bool _isSelected;
        private Action? _selectionChanged;
        private decimal _neuerWert;

        public int KontoId { get; init; }
        public int Kontonummer { get; init; }
        public string Bezeichnung { get; init; } = "";
        public string Richtung { get; init; } = "";
        public decimal? AlterWert { get; init; }
        public decimal Hochrechnung { get; init; }
        public decimal NeuerWert
        {
            get => _neuerWert;
            set
            {
                if (value < 0m)
                    throw new ArgumentOutOfRangeException(nameof(value), "Das neue Budget darf nicht negativ sein.");
                if (_neuerWert == value) return;

                _neuerWert = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Differenz));
                OnPropertyChanged(nameof(QuelleText));

                if (_selectionChanged != null)
                {
                    var sollAusgewaehltSein = Math.Abs(Differenz) >= 0.01m;
                    if (_isSelected != sollAusgewaehltSein)
                    {
                        _isSelected = sollAusgewaehltSein;
                        OnPropertyChanged(nameof(IsSelected));
                    }

                    _selectionChanged();
                }
            }
        }
        public decimal Differenz => NeuerWert - (AlterWert ?? 0m);
        public string QuelleText => Math.Abs(NeuerWert - Hochrechnung) >= 0.01m
            ? "Manuell"
            : "Hochrechnung";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                _selectionChanged?.Invoke();
            }
        }

        public void AuswahlInitialisieren(Action selectionChanged)
        {
            _selectionChanged = selectionChanged;
            IsSelected = Math.Abs(Differenz) >= 0.01m;
        }
    }

    public sealed class BudgetProjectionPreviewViewModel : BaseViewModel
    {
        public ObservableCollection<BudgetProjectionAdjustmentRow> Zeilen { get; } = new();
        public ICommand AlleCommand { get; }
        public ICommand KeineCommand { get; }
        public ICommand HochrechnungEinsetzenCommand { get; }
        public string ZeitraumInfo { get; }
        public string Datenqualitaet { get; }

        public BudgetProjectionPreviewViewModel(
            string zeitraum,
            int auswertungstage,
            int budgettage,
            IEnumerable<BudgetProjectionAdjustmentRow> zeilen)
        {
            ZeitraumInfo = $"Budgetzeitraum {zeitraum} · Hochrechnung aus {auswertungstage} von {budgettage} Tagen";
            Datenqualitaet = auswertungstage switch
            {
                < 30 => "Sehr kurze Datenbasis: Die Hochrechnung ist noch stark schwankungsanfällig.",
                < 90 => "Kurze Datenbasis: Einmalige und saisonale Buchungen können die Hochrechnung deutlich verzerren.",
                < 180 => "Mittlere Datenbasis: Prüfe saisonale oder ausserordentliche Positionen vor der Übernahme.",
                _ => "Breite Datenbasis: Die Hochrechnung ist als Erfahrungsbudget gut verwendbar; Sondereffekte trotzdem prüfen."
            };
            AlleCommand = new RelayCommand(_ => AuswahlSetzen(true));
            KeineCommand = new RelayCommand(_ => AuswahlSetzen(false));
            HochrechnungEinsetzenCommand = new RelayCommand(_ => HochrechnungenEinsetzen());

            foreach (var zeile in zeilen)
            {
                zeile.AuswahlInitialisieren(AuswahlAktualisiert);
                Zeilen.Add(zeile);
            }

            AuswahlAktualisiert();
        }

        public int Ausgewaehlt => Zeilen.Count(z => z.IsSelected && Math.Abs(z.Differenz) >= 0.01m);
        public bool HatAuswahl => Ausgewaehlt > 0;
        public decimal SummeAlt => Zeilen.Where(z => z.IsSelected).Sum(z => z.AlterWert ?? 0m);
        public decimal SummeNeu => Zeilen.Where(z => z.IsSelected).Sum(z => z.NeuerWert);
        public decimal Differenz => SummeNeu - SummeAlt;

        private void AuswahlSetzen(bool wert)
        {
            foreach (var zeile in Zeilen)
                zeile.IsSelected = wert && Math.Abs(zeile.Differenz) >= 0.01m;
            AuswahlAktualisiert();
        }

        private void HochrechnungenEinsetzen()
        {
            foreach (var zeile in Zeilen)
                zeile.NeuerWert = zeile.Hochrechnung;

            AuswahlAktualisiert();
        }

        private void AuswahlAktualisiert()
        {
            OnPropertyChanged(nameof(Ausgewaehlt));
            OnPropertyChanged(nameof(HatAuswahl));
            OnPropertyChanged(nameof(SummeAlt));
            OnPropertyChanged(nameof(SummeNeu));
            OnPropertyChanged(nameof(Differenz));
        }
    }
}
