using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace MyCoinFlow.ViewModels
{
    public class DashboardStweViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        private int _liegenschaftId;
        private string _liegenschaftName = "(keine)";
        private DateTime _von;
        private DateTime _bis;

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public string Header => $"STWE – Energie-Auswertung – {_von:dd.MM.yyyy}–{_bis:dd.MM.yyyy}";

        public ICommand RefreshCommand { get; }

        public ISeries[] EnergieKwhSeries { get; private set; } = Array.Empty<ISeries>();
        public Axis[] EnergieKwhXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] EnergieKwhYAxes { get; private set; } = Array.Empty<Axis>();

        public ISeries[] SolarAnteilSeries { get; private set; } = Array.Empty<ISeries>();
        public Axis[] SolarAnteilXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] SolarAnteilYAxes { get; private set; } = Array.Empty<Axis>();

        public ISeries[] KwhProOwnerSeries { get; private set; } = Array.Empty<ISeries>();
        public Axis[] KwhProOwnerXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] KwhProOwnerYAxes { get; private set; } = Array.Empty<Axis>();

        public ISeries[] ChfProOwnerSeries { get; private set; } = Array.Empty<ISeries>();
        public Axis[] ChfProOwnerXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] ChfProOwnerYAxes { get; private set; } = Array.Empty<Axis>();

        public DashboardStweViewModel()
        {
            // Sofort sichtbar, ob Binding funktioniert
            StatusText = "STWE: VM initialisiert …";

            RefreshCommand = new RelayCommand(_ => LoadAll());

            DetermineZeitraum();
            DetermineLiegenschaftWithData();

            LoadAll();
        }


        private void DetermineZeitraum()
        {
            // 1) aktiver Budgetzeitraum
            var aktivId = _db.HoleAktivenBudgetzeitraumId();
            if (aktivId.HasValue)
            {
                var bz = _db.HoleBudgetzeitraum(aktivId.Value);
                if (bz != null)
                {
                    _von = bz.Startdatum.Date;
                    _bis = bz.Enddatum.Date;
                    return;
                }
            }

            // Fallback: aktuelles Jahr
            _von = new DateTime(DateTime.Today.Year, 1, 1);
            _bis = new DateTime(DateTime.Today.Year, 12, 31);
        }

        private void DetermineLiegenschaftWithData()
        {
            var liegs = _db.StweLiegenschaftenGetAll();

            if (liegs == null || liegs.Count == 0)
            {
                _liegenschaftId = 0;
                _liegenschaftName = "(keine Liegenschaft)";
                return;
            }

            // Wähle die Liegenschaft, die im Zeitraum am meisten STWE-"Belege" hat.
            // (Das ist stabiler als "erste Liegenschaft".)
            MyCoinFlow.Models.StweLiegenschaft? best = null;
            int bestScore = -1;

            foreach (var l in liegs)
            {
                if (l.Id <= 0) continue;

                var setsInRange = _db.StweZaehlerdatenSetsGetByLiegenschaft(l.Id)
                    .Count(s => s.ErfasstAm.Date >= _von.Date && s.ErfasstAm.Date <= _bis.Date);

                var stweSetsInRange = _db.StweSetsGetByLiegenschaft(l.Id, _von, _bis).Count;

                // Score: STWE-Verteilungen zählen stärker als Zählerdaten (weil CHF-Chart darauf basiert)
                var score = (stweSetsInRange * 10) + setsInRange;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = l;
                }
            }

            best ??= liegs.First();

            _liegenschaftId = best.Id;
            _liegenschaftName = best.Name ?? $"Liegenschaft {best.Id}";
        }

        private void LoadAll()
        {
            OnPropertyChanged(nameof(Header));

            LoadEnergieKwhUndSolarAnteil();
            LoadKwhProOwner();
            LoadChfProOwner();

            UpdateStatusText();

            OnPropertyChanged(nameof(EnergieKwhSeries));
            OnPropertyChanged(nameof(EnergieKwhXAxes));
            OnPropertyChanged(nameof(EnergieKwhYAxes));

            OnPropertyChanged(nameof(SolarAnteilSeries));
            OnPropertyChanged(nameof(SolarAnteilXAxes));
            OnPropertyChanged(nameof(SolarAnteilYAxes));

            OnPropertyChanged(nameof(KwhProOwnerSeries));
            OnPropertyChanged(nameof(KwhProOwnerXAxes));
            OnPropertyChanged(nameof(KwhProOwnerYAxes));

            OnPropertyChanged(nameof(ChfProOwnerSeries));
            OnPropertyChanged(nameof(ChfProOwnerXAxes));
            OnPropertyChanged(nameof(ChfProOwnerYAxes));
        }

        private void UpdateStatusText()
        {
            if (_liegenschaftId <= 0)
            {
                StatusText = "Keine Liegenschaft gefunden.";
                return;
            }

            var setsTotal = _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId).Count;
            var setsInRange = _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId)
                .Count(s => s.ErfasstAm.Date >= _von.Date && s.ErfasstAm.Date <= _bis.Date);

            var stweSetsInRange = _db.StweSetsGetByLiegenschaft(_liegenschaftId, _von, _bis).Count;
            var ownerRows = _db.StweReportOwnerSummary(_liegenschaftId, _von, _bis).Count;
            var energiePoints = _db.StweEnergieChartGet(_liegenschaftId, _von, _bis).Count;

            StatusText =
                $"Liegenschaft: {_liegenschaftName} (Id {_liegenschaftId}) | " +
                $"ZählerdatenSets: {setsInRange}/{setsTotal} im Zeitraum | " +
                $"STWE-Sets (Verteilung): {stweSetsInRange} | " +
                $"OwnerSummary-Zeilen: {ownerRows} | " +
                $"Energie-Punkte: {energiePoints}";
        }

        private void LoadEnergieKwhUndSolarAnteil()
        {
            var qLabels = new[] { "Q1", "Q2", "Q3", "Q4" };

            EnergieKwhXAxes = new[] { new Axis { Labels = qLabels } };
            EnergieKwhYAxes = new[] { new Axis() };
            SolarAnteilXAxes = new[] { new Axis { Labels = qLabels } };
            SolarAnteilYAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100 } };

            // Default: 0-Werte
            double[] rechnungDelta = new double[4];
            double[] interneDelta = new double[4];
            double[] solarDelta = new double[4];

            if (_liegenschaftId <= 0)
            {
                EnergieKwhSeries = new ISeries[]
                {
            new ColumnSeries<double> { Name = "Rechnung kWh", Values = rechnungDelta },
            new ColumnSeries<double> { Name = "Interne kWh",  Values = interneDelta },
            new ColumnSeries<double> { Name = "Solar direkt", Values = solarDelta }
                };
                SolarAnteilSeries = new ISeries[] { new LineSeries<double> { Values = new double[] { 0, 0, 0, 0 } } };
                return;
            }

            // 1) Aus DB: Punkte (Label = "MM.yyyy") -> pro Quartal den *letzten* Stand nehmen
            var points = _db.StweEnergieChartGet(_liegenschaftId, _von, _bis);

            // Quartal -> letzter Stand (nicht Summe!)
            double?[] rechnungStand = new double?[4];
            double?[] interneStand = new double?[4];
            double?[] solarStand = new double?[4];
            DateTime?[] lastDt = new DateTime?[4];

            foreach (var p in points)
            {
                if (!DateTime.TryParseExact("01." + (p.Label ?? ""), "dd.MM.yyyy",
                    CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out var dt))
                    continue;

                if (dt.Date < _von.Date || dt.Date > _bis.Date) continue;

                int q = (dt.Month - 1) / 3; // 0..3

                // "letzter" Stand pro Quartal = Datum max
                if (!lastDt[q].HasValue || dt > lastDt[q].Value)
                {
                    lastDt[q] = dt;
                    rechnungStand[q] = (double)p.RechnungKwh;
                    interneStand[q] = (double)p.InterneKwh;
                    solarStand[q] = (double)p.SolarDirektKwh;
                }
            }

            // 2) Delta-Logik pro Quartal (kumulativ -> Verbrauch im Quartal)
            // Q1 = Stand(Q1)
            // Q2 = Stand(Q2) - Stand(Q1)
            // Q3 = Stand(Q3) - Stand(Q2)
            // Q4 = Stand(Q4) - Stand(Q3)
            for (int i = 0; i < 4; i++)
            {
                double curR = rechnungStand[i] ?? 0d;
                double curI = interneStand[i] ?? 0d;
                double curS = solarStand[i] ?? 0d;

                double prevR = (i == 0) ? 0d : (rechnungStand[i - 1] ?? 0d);
                double prevI = (i == 0) ? 0d : (interneStand[i - 1] ?? 0d);
                double prevS = (i == 0) ? 0d : (solarStand[i - 1] ?? 0d);

                var dR = curR - prevR;
                var dI = curI - prevI;
                var dS = curS - prevS;

                // defensiv: keine negativen Verbräuche anzeigen (z.B. Reset/fehlender Stand)
                rechnungDelta[i] = dR < 0 ? 0 : dR;
                interneDelta[i] = dI < 0 ? 0 : dI;
                solarDelta[i] = dS < 0 ? 0 : dS;
            }

            EnergieKwhSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Rechnung kWh", Values = rechnungDelta },
        new ColumnSeries<double> { Name = "Interne kWh",  Values = interneDelta },
        new ColumnSeries<double> { Name = "Solar direkt", Values = solarDelta }
            };

            // 3) Solar-Anteil % pro Quartal aus *Quartalswerten* (nicht aus kumulierten Ständen!)
            double[] solarPct = new double[4];
            for (int i = 0; i < 4; i++)
            {
                solarPct[i] = interneDelta[i] <= 0d ? 0d : (solarDelta[i] / interneDelta[i] * 100d);
            }

            SolarAnteilSeries = new ISeries[]
            {
        new LineSeries<double> { Values = solarPct }
            };
        }


        private void LoadKwhProOwner()
        {
            KwhProOwnerXAxes = new[] { new Axis { Labels = Array.Empty<string>(), LabelsRotation = 60, TextSize = 12 } };
            KwhProOwnerYAxes = new[] { new Axis() };
            KwhProOwnerSeries = new ISeries[] { new ColumnSeries<double> { Values = Array.Empty<double>() } };

            if (_liegenschaftId <= 0) return;

            var sets = _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId)
                .OrderByDescending(s => s.ErfasstAm)
                .ThenByDescending(s => s.Id)
                .ToList();

            var endSet = sets.FirstOrDefault(s => s.ErfasstAm.Date <= _bis.Date);
            if (endSet == null) return;

            var startCandidate = sets.FirstOrDefault(s => s.ErfasstAm.Date < _von.Date);
            var startSet = startCandidate ?? _db.StweZaehlerdatenGetPreviousSet(_liegenschaftId, endSet.ErfasstAm, endSet.Id);

            var endLines = _db.StweZaehlerdatenLinesGetBySet(endSet.Id);
            var startLines = startSet != null ? _db.StweZaehlerdatenLinesGetBySet(startSet.Id) : new List<StweZaehlerdatenLine>();
            var startDict = startLines.ToDictionary(x => x.ZaehlerId, x => x.NeuWert);

            var zaehler = _db.StweZaehlerGetByLiegenschaft(_liegenschaftId);
            var zaehlerDict = zaehler.ToDictionary(z => z.Id);

            var schluessel = _db.StweSchluesselGetByLiegenschaft(_liegenschaftId)
                .OrderBy(s => s.Id)
                .FirstOrDefault();

            var schluesselLines = schluessel != null
                ? _db.StweSchluesselLinesGet(schluessel.Id)
                : new List<MyCoinFlow.Models.StweSchluesselLine>();

            var ownerKwh = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            decimal allgHeizTotal = 0m;

            foreach (var l in endLines)
            {
                if (!zaehlerDict.TryGetValue(l.ZaehlerId, out var z))
                    continue;

                startDict.TryGetValue(l.ZaehlerId, out var startVal);
                var diff = l.NeuWert - startVal;
                if (diff <= 0m) continue;

                var typ = (z.Typ ?? "").Trim().ToUpperInvariant();

                if (typ == "DIREKT")
                {
                    if (!z.EinheitId.HasValue || z.EinheitId.Value <= 0) continue;

                    var owners = _db.StweEinheitEigentumGetByEinheit(z.EinheitId.Value)
                        .Where(o => o.GueltigVon.Date <= endSet.ErfasstAm.Date
                                 && (!o.GueltigBis.HasValue || o.GueltigBis.Value.Date >= endSet.ErfasstAm.Date))
                        .ToList();

                    var owner = owners.FirstOrDefault();
                    if (owner == null) continue;

                    Add(ownerKwh, owner.EigentuemerName, diff);
                }
                else if (typ == "ALLG" || typ == "HEIZ")
                {
                    allgHeizTotal += diff;
                }
            }

            if (allgHeizTotal > 0m && schluesselLines.Count > 0)
            {
                foreach (var sl in schluesselLines)
                {
                    if (sl.AnteilProzent <= 0m) continue;
                    var part = allgHeizTotal * (sl.AnteilProzent / 100m);
                    Add(ownerKwh, sl.EigentuemerName, part);
                }
            }

            var ordered = ownerKwh.OrderByDescending(kv => kv.Value).ToList();

            KwhProOwnerSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "kWh", Values = ordered.Select(x => (double)x.Value).ToArray() }
            };
            KwhProOwnerXAxes = new[]
            {
                new Axis { Labels = ordered.Select(x => x.Key).ToArray(), LabelsRotation = 60, TextSize = 12 }
            };
        }

        private void LoadChfProOwner()
        {
            ChfProOwnerXAxes = new[] { new Axis { Labels = Array.Empty<string>(), LabelsRotation = 60, TextSize = 12 } };
            ChfProOwnerYAxes = new[] { new Axis() };
            ChfProOwnerSeries = new ISeries[] { new ColumnSeries<double> { Values = Array.Empty<double>() } };

            if (_liegenschaftId <= 0) return;

            var rows = _db.StweReportOwnerSummary(_liegenschaftId, _von, _bis);
            var ordered = rows.OrderByDescending(r => Math.Abs(r.Summe)).ToList();

            ChfProOwnerSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "CHF", Values = ordered.Select(r => (double)r.Summe).ToArray() }
            };
            ChfProOwnerXAxes = new[]
            {
                new Axis { Labels = ordered.Select(r => r.EigentuemerName).ToArray(), LabelsRotation = 60, TextSize = 12 }
            };
        }

        private static void Add(Dictionary<string, decimal> dict, string key, decimal value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            dict.TryGetValue(key, out var cur);
            dict[key] = cur + value;
        }
    }
}
