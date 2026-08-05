using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Erkennt wiederkehrende Zahlungen (Abo-Kandidaten) aus bestehenden Transaktionen.
    ///
    /// Vorgehen:
    /// 1. Transaktionen pro Adresse gruppieren (Adresserkennung ist bereits gelaufen).
    /// 2. Innerhalb einer Adresse nach ähnlichem Betrag clustern (Toleranz relativ zum Median),
    ///    damit z.B. Einzelkäufe beim gleichen Händler das Abo nicht verwässern.
    /// 3. Abstände zwischen den Zahlungen prüfen: passt der Median-Abstand zu einer
    ///    bekannten Periodizität (monatlich/quartalsweise/halbjährlich/jährlich)?
    ///
    /// Der Service arbeitet rein in-memory und schreibt nichts in die DB —
    /// Kandidaten werden erst nach Bestätigung durch den Benutzer gespeichert.
    /// </summary>
    public static class AboErkennungService
    {
        private const decimal BetragClusterToleranz = 0.15m; // ±15 % um den Cluster-Median

        // Periodendefinition: (Code, minTage, maxTage, Mindestanzahl Zahlungen)
        private static readonly (string Code, int Min, int Max, int MinAnzahl)[] Perioden =
        {
            (AboPerioden.Monatlich,     24,  38, 3),
            (AboPerioden.Quartalsweise, 75, 105, 3),
            (AboPerioden.Halbjaehrlich, 160, 205, 2),
            (AboPerioden.Jaehrlich,     330, 400, 2)
        };

        /// <summary>
        /// Sucht Abo-Kandidaten. Bereits einem Abo zugeordnete Transaktionen werden
        /// übersprungen; Adressen mit bestehendem Abo werden NICHT ausgeschlossen,
        /// damit ein zweiter Vertrag beim gleichen Anbieter (z.B. zwei Telefon-Abos)
        /// als eigener Kandidat gefunden werden kann.
        /// </summary>
        public static List<AboKandidat> FindeKandidaten(
            List<Transaktion> alleMitAdresse,
            HashSet<int> bereitsZugeordneteTransaktionIds,
            HashSet<int> adressenMitBestehendemAbo)
        {
            var kandidaten = new List<AboKandidat>();

            var gruppen = alleMitAdresse
                .Where(t => t.AdresseId.HasValue
                            && !bereitsZugeordneteTransaktionIds.Contains(t.Id))
                .GroupBy(t => t.AdresseId!.Value);

            foreach (var gruppe in gruppen)
            {
                foreach (var cluster in ClusterNachBetrag(gruppe.ToList()))
                {
                    foreach (var kandidat in PruefeClusterMitSplit(cluster))
                    {
                        kandidat.AdresseHatAbo = adressenMitBestehendemAbo.Contains(kandidat.AdresseId);
                        kandidaten.Add(kandidat);
                    }
                }
            }

            return kandidaten
                .OrderByDescending(k => k.AnzahlZahlungen)
                .ThenBy(k => k.AdresseName)
                .ToList();
        }

        /// <summary>
        /// Prüft einen Betrags-Cluster. Enthält er auffällig viele Doppelzahlungen am
        /// selben Tag (Indiz für zwei Verträge beim gleichen Anbieter, z.B. zwei
        /// Telefon-Abos), wird er an der grössten Betragslücke geteilt und beide
        /// Hälften einzeln geprüft.
        /// </summary>
        private static IEnumerable<AboKandidat> PruefeClusterMitSplit(List<Transaktion> cluster)
        {
            var doppelTage = cluster
                .GroupBy(t => t.Datum.Date)
                .Count(g => g.Count() >= 2);

            if (doppelTage >= 2 && cluster.Count >= 6)
            {
                var sortiert = cluster.OrderBy(t => Math.Abs(t.Betrag)).ToList();

                int splitIndex = -1;
                decimal groessteLuecke = 0m;

                for (int i = 1; i < sortiert.Count; i++)
                {
                    var a = Math.Abs(sortiert[i - 1].Betrag);
                    var b = Math.Abs(sortiert[i].Betrag);
                    if (a <= 0m) continue;

                    var rel = (b - a) / a;
                    if (rel > groessteLuecke)
                    {
                        groessteLuecke = rel;
                        splitIndex = i;
                    }
                }

                if (splitIndex > 0)
                {
                    var teil1 = PruefePeriodizitaet(sortiert.Take(splitIndex).ToList());
                    var teil2 = PruefePeriodizitaet(sortiert.Skip(splitIndex).ToList());

                    if (teil1 != null && teil2 != null)
                    {
                        yield return teil1;
                        yield return teil2;
                        yield break;
                    }
                }
            }

            var einzel = PruefePeriodizitaet(cluster);
            if (einzel != null)
                yield return einzel;
        }

        /// <summary>
        /// Erkennt Lücken in der Zahlungsreihe eines Abos: Termine, an denen laut Rhythmus
        /// eine Zahlung hätte kommen müssen, aber keine zugeordnet ist.
        /// </summary>
        public static List<DateTime> FindeLuecken(List<DateTime> zahlungsDaten, int periodeTage)
        {
            var luecken = new List<DateTime>();

            if (zahlungsDaten.Count < 2 || periodeTage <= 0)
                return luecken;

            var daten = zahlungsDaten.Select(d => d.Date).OrderBy(d => d).ToList();

            for (int i = 1; i < daten.Count; i++)
            {
                var abstand = (daten[i] - daten[i - 1]).TotalDays;

                // Deutlich mehr als eine Periode Abstand => dazwischen fehlt mindestens eine Zahlung
                if (abstand < periodeTage * 1.6)
                    continue;

                int fehlend = (int)Math.Round(abstand / periodeTage) - 1;

                for (int k = 1; k <= fehlend; k++)
                    luecken.Add(daten[i - 1].AddDays(periodeTage * k));
            }

            return luecken;
        }

        /// <summary>
        /// Sucht für die Lücken eines Abos passende, noch nicht zugeordnete Transaktionen.
        /// Betrag (Toleranz) und Datumsnähe filtern grob vor; die Rangfolge entscheidet
        /// ein Punktesystem: Abo-Adresse ist das stärkste Signal, danach Text-Übereinstimmung
        /// im Buchungstext und das Buchungskonto. Eine Transaktion, die bereits einer
        /// ANDEREN Adresse gehört, bekommt einen kräftigen Abzug und wird nie vorselektiert
        /// (zufällig gleicher Betrag reicht nicht).
        /// </summary>
        public static List<AboLueckeKandidat> FindeLueckenKandidaten(
            Abo abo,
            List<DateTime> luecken,
            decimal referenzBetrag,
            List<Transaktion> nichtZugeordnete)
        {
            var result = new List<AboLueckeKandidat>();

            if (luecken.Count == 0 || referenzBetrag == 0m)
                return result;

            var periodeTage = AboPerioden.Tage(abo.Periodizitaet);
            var fensterTage = Math.Max(7, periodeTage / 3);

            // Toleranz des Abos, aber mindestens 10 % (schleichende Preiserhöhungen)
            var toleranz = Math.Max(10m, abo.BetragToleranzProzent) / 100m;

            var suchTokens = BaueSuchTokens(abo);

            foreach (var erwartet in luecken.OrderBy(d => d))
            {
                var kandidaten = nichtZugeordnete
                    .Where(t => Math.Abs((t.Datum.Date - erwartet).TotalDays) <= fensterTage)
                    .Where(t => Math.Abs(Math.Abs(t.Betrag) - Math.Abs(referenzBetrag))
                                <= Math.Abs(referenzBetrag) * toleranz)
                    .Select(t => BewerteKandidat(abo, t, erwartet, referenzBetrag, suchTokens))
                    .OrderByDescending(k => k.Punkte)
                    .ThenBy(k => Math.Abs((k.Datum - erwartet).TotalDays))
                    .Take(5)
                    .ToList();

                // Nur ein SICHERER bester Treffer wird vorselektiert:
                // Adresse passt, Text passt, oder zumindest kein Adress-Konflikt bei passendem Konto.
                var bester = kandidaten.FirstOrDefault();
                if (bester != null && bester.Punkte >= 50 && !bester.AdresseKonflikt)
                    bester.Uebernehmen = true;

                result.AddRange(kandidaten);
            }

            return result;
        }

        // Aussagekräftige Namensbestandteile des Abos/Anbieters für den Textvergleich
        private static List<string> BaueSuchTokens(Abo abo)
        {
            var stop = new[]
            {
                "AG", "GMBH", "SA", "SARL", "LTD", "INC", "CO",
                "DIE", "DER", "DAS", "UND", "VON", "FUER", "FÜR"
            };

            var quelle = string.Join(" ",
                new[] { abo.AdresseName, abo.Name }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            return System.Text.RegularExpressions.Regex
                .Matches(quelle.ToUpperInvariant(), @"[\p{L}\p{Nd}]{3,}")
                .Select(m => m.Value)
                .Where(w => !stop.Contains(w))
                .Distinct()
                .ToList();
        }

        private static AboLueckeKandidat BewerteKandidat(
            Abo abo,
            Transaktion t,
            DateTime erwartet,
            decimal referenzBetrag,
            List<string> suchTokens)
        {
            var gruende = new List<string>();
            int punkte = 0;

            bool adressePasst = t.AdresseId.HasValue
                                && abo.AdresseId.HasValue
                                && t.AdresseId == abo.AdresseId;

            bool adresseKonflikt = t.AdresseId.HasValue
                                   && abo.AdresseId.HasValue
                                   && t.AdresseId != abo.AdresseId;

            if (adressePasst)
            {
                punkte += 100;
                gruende.Add("Adresse passt");
            }
            else if (adresseKonflikt)
            {
                punkte -= 80;
                gruende.Add($"andere Adresse ({t.AdresseName})");
            }

            // Text-Übereinstimmung: Anbietername taucht im Buchungstext auf
            var text = ((t.Notiz ?? "") + " " + (t.AdresseName ?? "")).ToUpperInvariant();
            if (suchTokens.Count > 0 && suchTokens.Any(tok => text.Contains(tok)))
            {
                punkte += 50;
                gruende.Add("Buchungstext passt");
            }

            // Buchungskonto wie beim Abo erwartet
            var kandidatKonto = t.NachKontoId ?? t.VonKontoId;
            if (abo.ErwartetesKontoId.HasValue && kandidatKonto == abo.ErwartetesKontoId)
            {
                punkte += 30;
                gruende.Add("Konto passt");
            }
            else if (abo.ErwartetesKontoId.HasValue && kandidatKonto.HasValue)
            {
                punkte -= 10;
                gruende.Add("anderes Konto");
            }

            // Nähe zum erwarteten Datum (bis +15, nimmt pro Tag ab)
            var tageDiff = (int)Math.Abs((t.Datum.Date - erwartet).TotalDays);
            punkte += Math.Max(0, 15 - tageDiff);

            // Exakt gleicher Betrag als kleiner Bonus
            if (Math.Abs(t.Betrag) == Math.Abs(referenzBetrag))
                punkte += 5;

            return new AboLueckeKandidat
            {
                ErwartetAm = erwartet,
                TransaktionId = t.Id,
                Datum = t.Datum.Date,
                Betrag = t.Betrag,
                AdresseName = t.AdresseName,
                BankName = t.BankName,
                Notiz = t.Notiz,
                AdressePasst = adressePasst,
                AdresseKonflikt = adresseKonflikt,
                Punkte = punkte,
                MatchInfo = gruende.Count > 0 ? string.Join(", ", gruende) : "nur Betrag/Datum ähnlich",
                Uebernehmen = false
            };
        }

        // Gruppiert Transaktionen einer Adresse in Betrags-Cluster (greedy um den Median).
        private static List<List<Transaktion>> ClusterNachBetrag(List<Transaktion> transaktionen)
        {
            var cluster = new List<List<Transaktion>>();
            var rest = transaktionen
                .OrderBy(t => Math.Abs(t.Betrag))
                .ToList();

            while (rest.Count > 0)
            {
                var referenz = Math.Abs(Median(rest.Select(t => Math.Abs(t.Betrag)).ToList()));
                if (referenz == 0m) referenz = 0.01m;

                var passend = rest
                    .Where(t => Math.Abs(Math.Abs(t.Betrag) - referenz) <= referenz * BetragClusterToleranz)
                    .ToList();

                if (passend.Count == 0)
                {
                    // Sicherheitsnetz gegen Endlosschleife: grössten Ausreisser einzeln abspalten
                    passend = new List<Transaktion> { rest[^1] };
                }

                cluster.Add(passend);
                rest = rest.Except(passend).ToList();
            }

            return cluster;
        }

        // Prüft, ob die Zahlungsabstände eines Clusters zu einer Periodizität passen.
        private static AboKandidat? PruefePeriodizitaet(List<Transaktion> cluster)
        {
            if (cluster.Count < 2)
                return null;

            var sortiert = cluster.OrderBy(t => t.Datum).ToList();

            var abstaende = new List<int>();
            for (int i = 1; i < sortiert.Count; i++)
                abstaende.Add((int)(sortiert[i].Datum.Date - sortiert[i - 1].Datum.Date).TotalDays);

            // Doppelzahlungen am selben Tag (z.B. Teilbeträge) nicht als Periode werten
            abstaende = abstaende.Where(a => a > 0).ToList();
            if (abstaende.Count == 0)
                return null;

            var medianAbstand = (int)Median(abstaende.Select(a => (decimal)a).ToList());

            foreach (var (code, min, max, minAnzahl) in Perioden)
            {
                if (medianAbstand < min || medianAbstand > max)
                    continue;

                if (sortiert.Count < minAnzahl)
                    continue;

                // Mehrheit der Abstände muss ins Raster passen (Ausreisser erlaubt)
                var passend = abstaende.Count(a => a >= min && a <= max);
                if (passend * 2 < abstaende.Count)
                    continue;

                var kontoIds = sortiert
                    .Select(t => t.NachKontoId ?? t.VonKontoId)
                    .Where(k => k.HasValue)
                    .Select(k => k!.Value)
                    .ToList();

                var haeufigstesKonto = kontoIds.Count > 0
                    ? kontoIds.GroupBy(k => k).OrderByDescending(g => g.Count()).First().Key
                    : (int?)null;

                return new AboKandidat
                {
                    AdresseId = sortiert[0].AdresseId!.Value,
                    AdresseName = sortiert[0].AdresseName ?? $"Adresse #{sortiert[0].AdresseId}",
                    Periodizitaet = code,
                    MedianBetrag = Median(sortiert.Select(t => Math.Abs(t.Betrag)).ToList()),
                    AnzahlZahlungen = sortiert.Count,
                    ErsteZahlung = sortiert[0].Datum.Date,
                    LetzteZahlung = sortiert[^1].Datum.Date,
                    HaeufigstesKontoId = haeufigstesKonto,
                    MehrereKonten = kontoIds.Distinct().Count() > 1,
                    TransaktionIds = sortiert.Select(t => t.Id).ToList()
                };
            }

            return null;
        }

        private static decimal Median(List<decimal> werte)
        {
            if (werte.Count == 0) return 0m;

            var s = werte.OrderBy(v => v).ToList();
            int mitte = s.Count / 2;

            return s.Count % 2 == 0
                ? (s[mitte - 1] + s[mitte]) / 2m
                : s[mitte];
        }
    }
}
