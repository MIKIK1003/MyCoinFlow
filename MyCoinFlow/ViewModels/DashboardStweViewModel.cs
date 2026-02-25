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

            // Zählerstamm (Typ) + ZählerdatenSets im Zeitraum (+ 1 Set davor als Basis)
            var zaehler = _db.StweZaehlerGetByLiegenschaft(_liegenschaftId);
            var zaehlerById = zaehler.ToDictionary(z => z.Id);

            var setsAll = _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId)
                .OrderBy(s => s.ErfasstAm)
                .ThenBy(s => s.Id)
                .ToList();

            if (setsAll.Count == 0) return;

            // Wir wollen Delta-Werte im Zeitraum: dafür brauchen wir das letzte Set vor _von als Startbasis.
            var inRange = setsAll.Where(s => s.ErfasstAm.Date >= _von.Date && s.ErfasstAm.Date <= _bis.Date).ToList();
            if (inRange.Count == 0) return;

            var baseSet = setsAll.LastOrDefault(s => s.ErfasstAm.Date < _von.Date) ?? inRange.First();

            // Wir berechnen Deltas zwischen aufeinanderfolgenden Sets, und addieren sie ins Quartal des "aktuellen" Sets.
            StweZaehlerdatenSet? prev = null;
            foreach (var cur in setsAll)
            {
                if (cur.Id == baseSet.Id)
                {
                    prev = cur;
                    continue;
                }

                // Nur Deltas zählen, deren "cur" im Zeitraum liegt
                if (cur.ErfasstAm.Date < _von.Date || cur.ErfasstAm.Date > _bis.Date)
                {
                    prev = cur;
                    continue;
                }

                if (prev == null)
                {
                    prev = cur;
                    continue;
                }

                int q = (cur.ErfasstAm.Month - 1) / 3; // 0..3

                var curLines = _db.StweZaehlerdatenLinesGetBySet(cur.Id).ToDictionary(x => x.ZaehlerId, x => x.NeuWert);
                var prevLines = _db.StweZaehlerdatenLinesGetBySet(prev.Id).ToDictionary(x => x.ZaehlerId, x => x.NeuWert);

                decimal evu = 0m;
                decimal internSum = 0m;

                foreach (var kv in curLines)
                {
                    var zaehlerId = kv.Key;
                    var curVal = kv.Value;
                    prevLines.TryGetValue(zaehlerId, out var prevVal);

                    var diff = curVal - prevVal;
                    if (diff <= 0m) continue;

                    if (!zaehlerById.TryGetValue(zaehlerId, out var z))
                        continue;

                    var typ = (z.Typ ?? "").Trim().ToUpperInvariant();

                    if (typ == "EVU")
                        evu += diff;
                    else
                        internSum += diff;
                }

                // Solar direkt ist nur der Überschuss, wenn intern > EVU
                var solar = Math.Max(0m, internSum - evu);

                rechnungDelta[q] += (double)evu;
                interneDelta[q] += (double)internSum;
                solarDelta[q] += (double)solar;

                prev = cur;
            }

            EnergieKwhSeries = new ISeries[]
            {
        new ColumnSeries<double> { Name = "Rechnung kWh", Values = rechnungDelta },
        new ColumnSeries<double> { Name = "Interne kWh",  Values = interneDelta },
        new ColumnSeries<double> { Name = "Solar direkt", Values = solarDelta }
            };

            double[] solarPct = new double[4];
            for (int i = 0; i < 4; i++)
                solarPct[i] = interneDelta[i] <= 0d ? 0d : (solarDelta[i] / interneDelta[i] * 100d);

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

            // Zeitraum: wir nehmen "letztes Set <= bis" und "Set davor als Start"
            var sets = _db.StweZaehlerdatenSetsGetByLiegenschaft(_liegenschaftId)
                .OrderBy(s => s.ErfasstAm)
                .ThenBy(s => s.Id)
                .ToList();

            var endSet = sets.LastOrDefault(s => s.ErfasstAm.Date <= _bis.Date);
            if (endSet == null) return;

            var startSet = sets.LastOrDefault(s => s.ErfasstAm.Date < _von.Date)
                           ?? _db.StweZaehlerdatenGetPreviousSet(_liegenschaftId, endSet.ErfasstAm, endSet.Id);

            if (startSet == null) return;

            var endLines = _db.StweZaehlerdatenLinesGetBySet(endSet.Id);
            var startLines = _db.StweZaehlerdatenLinesGetBySet(startSet.Id);
            var startDict = startLines.ToDictionary(x => x.ZaehlerId, x => x.NeuWert);

            var zaehler = _db.StweZaehlerGetByLiegenschaft(_liegenschaftId);
            var zaehlerDict = zaehler.ToDictionary(z => z.Id);

            var ownerKwh = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var l in endLines)
            {
                if (!zaehlerDict.TryGetValue(l.ZaehlerId, out var z))
                    continue;

                var typ = (z.Typ ?? "").Trim().ToUpperInvariant();
                if (typ == "EVU") continue; // EVU ist Referenz, wird nicht verteilt

                startDict.TryGetValue(l.ZaehlerId, out var startVal);
                var diff = l.NeuWert - startVal;
                if (diff <= 0m) continue;

                // NEU: Verteilung immer über Zähler-Zeilen (StweZaehlerLine)
                var zLines = _db.StweZaehlerLinesGet(z.Id);
                var sumPct = zLines.Sum(x => Math.Max(0m, x.AnteilProzent));
                if (sumPct <= 0m) continue;

                foreach (var zl in zLines)
                {
                    var pct = Math.Max(0m, zl.AnteilProzent);
                    if (pct <= 0m) continue;

                    var part = diff * (pct / sumPct);
                    Add(ownerKwh, zl.EigentuemerName, part);
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

            // NUR Nummernkreis 2 (Ausgaben): Kontonummer 20000–29999
            var rows = _db.StweReportOwnerSummaryNr2Ausgaben(_liegenschaftId, _von, _bis);
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
