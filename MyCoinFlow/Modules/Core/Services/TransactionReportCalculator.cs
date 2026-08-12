using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCoinFlow.Services
{
    public sealed class TransactionReportCalculator
    {
        public TransactionReportResult Berechnen(
            TransactionReportOptions optionen,
            IReadOnlyCollection<TransactionReportAccount> konten,
            IReadOnlyCollection<Transaktion> transaktionen)
        {
            if (optionen.AuswertungBis.Date < optionen.AuswertungVon.Date)
                throw new ArgumentException("Das Bis-Datum darf nicht vor dem Von-Datum liegen.");

            if (optionen.BudgetBis.Date < optionen.BudgetVon.Date)
                throw new ArgumentException("Der Budgetzeitraum ist ungültig.");

            var auswertungstage = (optionen.AuswertungBis.Date - optionen.AuswertungVon.Date).Days + 1;
            var budgettage = (optionen.BudgetBis.Date - optionen.BudgetVon.Date).Days + 1;

            if (auswertungstage <= 0 || budgettage <= 0)
                throw new ArgumentException("Die Zeiträume müssen mindestens einen Tag enthalten.");

            var einzelzeilen = konten
                .OrderBy(k => k.Kontonummer)
                .Select(k => BerechneKonto(optionen, k, transaktionen, auswertungstage, budgettage))
                .ToList();

            var gruppierteZeilen = Gruppieren(optionen.Gruppierung, konten, einzelzeilen);
            var kontenOhneBudget = konten.Count(k => !k.Jahresbudget.HasValue);

            return new TransactionReportResult
            {
                Optionen = optionen,
                Zeilen = gruppierteZeilen,
                EinzelkontoZeilen = einzelzeilen,
                GroessteAusgaben = ErstelleSpotlight(
                    einzelzeilen, konten, TransactionReportDirection.Ausgabe, optionen.Modus),
                GroessteEinnahmen = ErstelleSpotlight(
                    einzelzeilen, konten, TransactionReportDirection.Einnahme, optionen.Modus),
                GroessteAbweichungen = einzelzeilen
                    .Where(z => z.DeltaJahr.HasValue && z.DeltaJahr.Value < 0m)
                    .OrderBy(z => z.DeltaJahr)
                    .Take(5)
                    .ToList(),
                Einnahmen = SummiereRichtung(einzelzeilen, konten, TransactionReportDirection.Einnahme),
                Ausgaben = SummiereRichtung(einzelzeilen, konten, TransactionReportDirection.Ausgabe),
                AusgewaehlteKonten = konten.Count,
                KontenOhneBudget = kontenOhneBudget,
                Auswertungstage = auswertungstage,
                Budgettage = budgettage,
                BudgetabdeckungProzent = konten.Count == 0
                    ? 0m
                    : Runde((konten.Count - kontenOhneBudget) * 100m / konten.Count),
                ErstelltAm = DateTime.Now
            };
        }

        private static IReadOnlyList<TransactionReportSpotlightRow> ErstelleSpotlight(
            IReadOnlyCollection<TransactionReportRow> zeilen,
            IReadOnlyCollection<TransactionReportAccount> konten,
            TransactionReportDirection richtung,
            TransactionReportMode modus)
        {
            var ids = konten.Where(k => k.Richtung == richtung).Select(k => k.KontoId).ToHashSet();
            var basis = zeilen
                .Where(z => z.KontoId.HasValue && ids.Contains(z.KontoId.Value))
                .Select(z => new
                {
                    Zeile = z,
                    Betrag = modus == TransactionReportMode.NurBudget
                        ? z.BudgetJahr ?? 0m
                        : z.IstZeitraum ?? 0m,
                    Hochrechnung = modus == TransactionReportMode.NurBudget
                        ? (decimal?)null
                        : z.HochrechnungJahr
                })
                .Where(x => x.Betrag > 0m || (x.Hochrechnung ?? 0m) > 0m)
                .ToList();

            var gesamt = basis.Where(x => x.Betrag > 0m).Sum(x => x.Betrag);
            var hochrechnungGesamt = basis
                .Where(x => (x.Hochrechnung ?? 0m) > 0m)
                .Sum(x => x.Hochrechnung ?? 0m);

            var rangliste = basis
                .OrderByDescending(x => x.Hochrechnung ?? x.Betrag)
                .ToList();

            return rangliste
                .Take(5)
                .Select((x, index) => new TransactionReportSpotlightRow
                {
                    Rang = index + 1,
                    KontoId = x.Zeile.KontoId!.Value,
                    Konto = x.Zeile.Konto,
                    Bezeichnung = x.Zeile.Bezeichnung,
                    Betrag = x.Betrag,
                    AnteilProzent = gesamt == 0m || x.Betrag <= 0m
                        ? 0m
                        : Runde(x.Betrag / gesamt * 100m),
                    HochrechnungJahr = x.Hochrechnung,
                    HochrechnungAnteilProzent = hochrechnungGesamt == 0m || (x.Hochrechnung ?? 0m) <= 0m
                        ? null
                        : Runde(x.Hochrechnung!.Value / hochrechnungGesamt * 100m)
                })
                .ToList();
        }

        private static TransactionReportRow BerechneKonto(
            TransactionReportOptions optionen,
            TransactionReportAccount konto,
            IReadOnlyCollection<Transaktion> transaktionen,
            int auswertungstage,
            int budgettage)
        {
            var zeigtBudget = optionen.Modus != TransactionReportMode.IstMitHochrechnung;
            var zeigtIst = optionen.Modus != TransactionReportMode.NurBudget;
            var zeigtDelta = optionen.Modus == TransactionReportMode.SollIstMitHochrechnung;

            var budgetJahr = zeigtBudget ? konto.Jahresbudget ?? 0m : (decimal?)null;
            var soll = zeigtBudget
                ? Runde((konto.Jahresbudget ?? 0m) * auswertungstage / budgettage)
                : (decimal?)null;

            var ist = zeigtIst
                ? Runde(BerechneIst(konto, transaktionen))
                : (decimal?)null;

            var hochrechnung = zeigtIst
                ? RundeHochrechnung((ist ?? 0m) * budgettage / auswertungstage)
                : (decimal?)null;

            decimal? deltaZeitraum = null;
            decimal? deltaJahr = null;
            if (zeigtDelta && konto.Richtung != TransactionReportDirection.Neutral)
            {
                var faktor = konto.Richtung == TransactionReportDirection.Ausgabe ? 1m : -1m;
                deltaZeitraum = Runde(((soll ?? 0m) - (ist ?? 0m)) * faktor);
                deltaJahr = Runde(((budgetJahr ?? 0m) - (hochrechnung ?? 0m)) * faktor);
            }

            decimal? erfuellung = null;
            if (zeigtDelta && soll.HasValue && soll.Value != 0m && ist.HasValue)
                erfuellung = Runde(ist.Value / soll.Value * 100m);

            return new TransactionReportRow
            {
                KontoId = konto.KontoId,
                Konto = konto.Kontonummer.ToString("D4"),
                Bezeichnung = konto.Detail,
                Richtung = RichtungText(konto.Richtung),
                BudgetJahr = budgetJahr,
                SollZeitraum = soll,
                IstZeitraum = ist,
                HochrechnungJahr = hochrechnung,
                DeltaZeitraum = deltaZeitraum,
                DeltaJahr = deltaJahr,
                ErfuellungProzent = erfuellung
            };
        }

        private static decimal BerechneIst(
            TransactionReportAccount konto,
            IReadOnlyCollection<Transaktion> transaktionen)
        {
            decimal einnahmen = 0m;
            decimal ausgaben = 0m;

            foreach (var transaktion in transaktionen)
            {
                if (transaktion.VonKontoId != konto.KontoId && transaktion.NachKontoId != konto.KontoId)
                    continue;

                if (transaktion.VonKontoId == konto.KontoId && transaktion.NachKontoId == null)
                {
                    einnahmen += transaktion.Betrag;
                    continue;
                }

                if (transaktion.VonKontoId == null && transaktion.NachKontoId == konto.KontoId)
                {
                    if (konto.Richtung == TransactionReportDirection.Einnahme)
                        einnahmen += transaktion.Betrag;
                    else
                        ausgaben += transaktion.Betrag;

                    continue;
                }

                if (IstAusgabeFuerKonto(konto, transaktion))
                    ausgaben += transaktion.Betrag;
                else
                    einnahmen += transaktion.Betrag;
            }

            return konto.Richtung == TransactionReportDirection.Einnahme
                ? einnahmen - ausgaben
                : ausgaben - einnahmen;
        }

        private static bool IstAusgabeFuerKonto(TransactionReportAccount konto, Transaktion transaktion)
        {
            if (string.Equals(transaktion.ImportQuelle, "KreditkartenExcel", StringComparison.OrdinalIgnoreCase))
            {
                if (transaktion.VonKontoId == konto.KontoId && transaktion.NachKontoId != null)
                    return false;

                if (konto.Richtung != TransactionReportDirection.Einnahme)
                    return true;
            }

            if (transaktion.VonKontoId == konto.KontoId)
                return true;

            if (transaktion.NachKontoId == konto.KontoId)
                return false;

            return konto.Richtung != TransactionReportDirection.Einnahme;
        }

        private static IReadOnlyList<TransactionReportRow> Gruppieren(
            TransactionReportGrouping gruppierung,
            IReadOnlyCollection<TransactionReportAccount> konten,
            IReadOnlyCollection<TransactionReportRow> zeilen)
        {
            if (gruppierung == TransactionReportGrouping.Einzelkonto)
                return zeilen.OrderBy(z => z.Konto).ToList();

            var kontoNachId = konten.ToDictionary(k => k.KontoId);

            string Gruppenschluessel(TransactionReportAccount konto) => gruppierung switch
            {
                TransactionReportGrouping.Art => konto.Art,
                TransactionReportGrouping.Gruppe => konto.Gruppe,
                _ => konto.Untergruppe
            };

            return zeilen
                .Where(z => z.KontoId.HasValue && kontoNachId.ContainsKey(z.KontoId.Value))
                .GroupBy(z =>
                {
                    var konto = kontoNachId[z.KontoId!.Value];
                    var key = Gruppenschluessel(konto);
                    return new
                    {
                        Bezeichnung = string.IsNullOrWhiteSpace(key) ? "(ohne Bezeichnung)" : key.Trim(),
                        konto.Richtung
                    };
                })
                .Select(g =>
                {
                    var kontoAb = g.Min(z => kontoNachId[z.KontoId!.Value].Kontonummer);
                    return AggregiereGruppe(g.Key.Bezeichnung, g.Key.Richtung, kontoAb, g);
                })
                .OrderBy(z => z.Richtung)
                .ThenBy(z => int.TryParse(z.Konto, out var kontonummer)
                    ? kontonummer
                    : int.MaxValue)
                .ToList();
        }

        private static TransactionReportRow AggregiereGruppe(
            string bezeichnung,
            TransactionReportDirection richtung,
            int kontoAb,
            IEnumerable<TransactionReportRow> zeilen)
        {
            var liste = zeilen.ToList();
            var soll = SummeOderNull(liste.Select(z => z.SollZeitraum));
            var ist = SummeOderNull(liste.Select(z => z.IstZeitraum));

            return new TransactionReportRow
            {
                Konto = kontoAb.ToString("D4"),
                Bezeichnung = bezeichnung,
                Richtung = RichtungText(richtung),
                BudgetJahr = SummeOderNull(liste.Select(z => z.BudgetJahr)),
                SollZeitraum = soll,
                IstZeitraum = ist,
                HochrechnungJahr = SummeOderNull(liste.Select(z => z.HochrechnungJahr)),
                DeltaZeitraum = SummeOderNull(liste.Select(z => z.DeltaZeitraum)),
                DeltaJahr = SummeOderNull(liste.Select(z => z.DeltaJahr)),
                ErfuellungProzent = soll.HasValue && soll.Value != 0m && ist.HasValue
                    ? Runde(ist.Value / soll.Value * 100m)
                    : null
            };
        }

        private static TransactionReportDirectionSummary SummiereRichtung(
            IReadOnlyCollection<TransactionReportRow> zeilen,
            IReadOnlyCollection<TransactionReportAccount> konten,
            TransactionReportDirection richtung)
        {
            var ids = konten.Where(k => k.Richtung == richtung).Select(k => k.KontoId).ToHashSet();
            var passende = zeilen.Where(z => z.KontoId.HasValue && ids.Contains(z.KontoId.Value)).ToList();

            return new TransactionReportDirectionSummary
            {
                BudgetJahr = SummeOderNull(passende.Select(z => z.BudgetJahr)),
                SollZeitraum = SummeOderNull(passende.Select(z => z.SollZeitraum)),
                IstZeitraum = SummeOderNull(passende.Select(z => z.IstZeitraum)),
                HochrechnungJahr = SummeOderNull(passende.Select(z => z.HochrechnungJahr)),
                DeltaZeitraum = SummeOderNull(passende.Select(z => z.DeltaZeitraum)),
                DeltaJahr = SummeOderNull(passende.Select(z => z.DeltaJahr))
            };
        }

        private static decimal? SummeOderNull(IEnumerable<decimal?> werte)
        {
            var liste = werte.ToList();
            return liste.Any(w => w.HasValue)
                ? Runde(liste.Where(w => w.HasValue).Sum(w => w!.Value))
                : null;
        }

        private static string RichtungText(TransactionReportDirection richtung) => richtung switch
        {
            TransactionReportDirection.Einnahme => "Einnahme",
            TransactionReportDirection.Ausgabe => "Ausgabe",
            _ => "Neutral"
        };

        private static decimal Runde(decimal wert) => Math.Round(wert, 2, MidpointRounding.AwayFromZero);

        internal static decimal RundeHochrechnung(decimal wert)
        {
            var absolut = Math.Abs(wert);
            var einheit = absolut switch
            {
                < 100m => 1m,
                < 1000m => 10m,
                < 10000m => 100m,
                _ => 1000m
            };

            return Math.Round(wert / einheit, 0, MidpointRounding.AwayFromZero) * einheit;
        }
    }
}
