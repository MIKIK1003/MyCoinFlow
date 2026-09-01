using MyCoinFlow.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Erkennt wiederkehrende Zahlungsserien aus bestehenden Transaktionen.
    ///
    /// Vorgehen:
    /// 1. Transaktionen pro Adresse gruppieren (Adresserkennung ist bereits gelaufen).
    /// 2. Innerhalb einer Adresse nach ähnlichem Betrag clustern (Toleranz relativ zum Median),
    ///    damit z.B. Einzelkäufe beim gleichen Händler das Abo nicht verwässern.
    /// 3. Abstände zwischen den Zahlungen prüfen: passt der Median-Abstand zu einer
    ///    bekannten Periodizität (monatlich/quartalsweise/halbjährlich/jährlich)?
    /// 4. Richtung und Themenart bestimmen. Eindeutige Streaming-/Lizenzhinweise
    ///    dürfen vorsichtig bereits vor einem vollständig bestätigten Rhythmus erscheinen.
    ///
    /// Der Service arbeitet rein in-memory und schreibt nichts in die DB —
    /// Kandidaten werden erst nach Bestätigung durch den Benutzer gespeichert.
    /// </summary>
    public static class AboErkennungService
    {
        private const decimal BetragClusterToleranz = 0.15m; // ±15 % um den Cluster-Median

        private enum AboHinweisStaerke
        {
            KeinHinweis,
            Moeglich,
            Stark
        }

        // Bekannte Themenmerkmale dienen der Sortierung, nicht als Voraussetzung:
        // Ein stabiler Rhythmus ist selbst eine relevante Zahlungsserie.
        private static readonly string[] StreamingMerkmale =
        {
            "NETFLIX", "SPOTIFY", "DISNEY+", "DISNEY PLUS", "YOUTUBE PREMIUM",
            "APPLE MUSIC", "AMAZON PRIME", "PRIME VIDEO", "DEEZER", "AUDIBLE",
            "TIDAL", "TWITCH", "PATREON", "MUBI", "DAZN", "SKY SHOW", "SKYSHOW", "PARAMOUNT+", "PARAMOUNT PLUS", "PLAYSTATION PLUS",
            "XBOX GAME PASS", "NINTENDO SWITCH ONLINE", "HBO", "CRUNCHYROLL", "SOUNDCLOUD GO"
        };

        private static readonly string[] SoftwareLizenzMerkmale =
        {
            "APPLE.COM/BILL", "APPLE COM BILL", "APP STORE", "APPSTORE", "GOOGLE PLAY",
            "GOOGLE ONE", "MICROSOFT 365", "MICROSOFT365", "OFFICE 365", "OFFICE365",
            "ADOBE", "OPENAI", "CHATGPT", "DROPBOX", "ICLOUD", "ONEDRIVE",
            "CANVA", "NOTION", "EVERNOTE", "GITHUB", "JETBRAINS", "1PASSWORD",
            "LASTPASS", "NORDVPN", "SURFSHARK", "MCAFEE", "NORTON", "ANTIVIRUS"
        };

        // Gattungsbegriffe sind nur zusammen mit einem bestätigten Zahlungsrhythmus
        // ausreichend. Ein blosses Wort wie "Abo" genügt bewusst nicht.
        private static readonly string[] StreamingGattungen =
        {
            "STREAMING", "MUSIK STREAM", "VIDEO STREAM", "PODCAST PREMIUM"
        };

        private static readonly string[] SoftwareLizenzGattungen =
        {
            "SOFTWARE", "SAAS", "CLOUD SPEICHER", "CLOUD STORAGE", "SOFTWARELIZENZ",
            "MONATSLIZENZ", "JAHRESLIZENZ", "APP LIZENZ", "APP SUBSCRIPTION"
        };

        private static readonly (string Kategorie, string[] Merkmale)[] KategorieMerkmale =
        {
            (AboKategorien.Wohnen, new[] { "MIETE", "MIETZINS", "PACHT", "NEBENKOSTEN" }),
            (AboKategorien.Versicherung, new[] { "VERSICHERUNG", "POLICE", "KRANKENKASSE" }),
            (AboKategorien.Telekommunikation, new[] { "MOBILFUNK", "TELEKOM", "INTERNET" }),
            (AboKategorien.Mitgliedschaft, new[] { "FITNESS", "GYM", "MITGLIEDSCHAFT", "VEREIN" }),
            (AboKategorien.Finanzierung, new[] { "LEASING", "DARLEHEN", "HYPOTHEK", "KREDIT" }),
            (AboKategorien.SteuernGebuehren, new[] { "STEUERRECHNUNG", "STEUERRATE", "GEMEINDESTEUER", "KANTONSSTEUER" }),
            (AboKategorien.VorsorgeSparen, new[] { "SPARPLAN", "VORSORGE", "SAEULE 3", "SÄULE 3", "PENSIONSKASSE" }),
            (AboKategorien.Vertrag, new[] { "VERTRAG", "ABONNEMENT" })
        };

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
            HashSet<int> adressenMitBestehendemAbo,
            IReadOnlyCollection<AboKandidatAusschluss>? ignorierteKandidaten = null,
            Func<int, bool>? istEinnahmenKonto = null)
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
                    var hinweis = BewerteAboHinweis(cluster);
                    var richtung = RichtungErmitteln(cluster, istEinnahmenKonto);

                    var periodischeKandidaten = PruefeClusterMitSplit(cluster).ToList();
                    foreach (var kandidat in periodischeKandidaten)
                    {
                        kandidat.AdresseHatAbo = adressenMitBestehendemAbo.Contains(kandidat.AdresseId);
                        kandidat.Kategorie = hinweis.Kategorie;
                        kandidat.Richtung = richtung;
                        kandidat.Erkennungsgrund = $"{hinweis.Grund}; Zahlungsrhythmus bestätigt ({Zahlungsrichtungen.Anzeige(richtung)})";
                        kandidaten.Add(kandidat);
                    }

                    if (periodischeKandidaten.Count == 0 && hinweis.Staerke == AboHinweisStaerke.Stark)
                    {
                        var kandidat = BaueSemantischenKandidaten(cluster, hinweis.Grund, hinweis.Kategorie, richtung);
                        kandidat.AdresseHatAbo = adressenMitBestehendemAbo.Contains(kandidat.AdresseId);
                        kandidaten.Add(kandidat);
                    }
                }
            }

            return kandidaten
                .Where(k => !IstIgnoriert(k, ignorierteKandidaten))
                .OrderBy(k => k.Richtung == Zahlungsrichtungen.Einnahme ? 0 : 1)
                .ThenBy(k => k.Kategorie)
                .ThenBy(k => k.RhythmusNurVermutet)
                .ThenByDescending(k => k.AnzahlZahlungen)
                .ThenBy(k => k.AdresseName)
                .ToList();
        }

        private static (AboHinweisStaerke Staerke, string Grund, string Kategorie) BewerteAboHinweis(List<Transaktion> cluster)
        {
            var text = string.Join(" ", cluster.Select(t => $"{t.AdresseName} {t.Notiz}"))
                .ToUpperInvariant();

            var streaming = StreamingMerkmale.FirstOrDefault(text.Contains);
            if (streaming != null)
                return (AboHinweisStaerke.Stark, $"typischer Streaminganbieter ({streaming})", AboKategorien.Streaming);

            var software = SoftwareLizenzMerkmale.FirstOrDefault(text.Contains);
            if (software != null)
                return (AboHinweisStaerke.Stark, $"typischer App-/Softwareanbieter ({software})", AboKategorien.SoftwareLizenz);

            var streamingGattung = StreamingGattungen.FirstOrDefault(text.Contains);
            if (streamingGattung != null)
                return (AboHinweisStaerke.Moeglich, $"Streaming-Hinweis ({streamingGattung})", AboKategorien.Streaming);

            var softwareGattung = SoftwareLizenzGattungen.FirstOrDefault(text.Contains);
            if (softwareGattung != null)
                return (AboHinweisStaerke.Moeglich, $"Software-/Lizenzhinweis ({softwareGattung})", AboKategorien.SoftwareLizenz);

            var themenTreffer = KategorieMerkmale
                .SelectMany(value => value.Merkmale.Select(merkmal => (value.Kategorie, Merkmal: merkmal)))
                .FirstOrDefault(value => text.Contains(value.Merkmal));
            return themenTreffer.Merkmal != null
                ? (AboHinweisStaerke.Moeglich, $"thementypischer Buchungstext ({themenTreffer.Merkmal})", themenTreffer.Kategorie)
                : (AboHinweisStaerke.Moeglich, "regelmässige Zahlungsserie", AboKategorien.Sonstige);
        }

        /// <summary>
        /// Ordnet bekannte Bestandsdaten einer Themenart zu.
        /// </summary>
        public static string KategorieErmitteln(string? text)
        {
            var normalized = (text ?? string.Empty).ToUpperInvariant();
            if (StreamingMerkmale.Any(normalized.Contains) || StreamingGattungen.Any(normalized.Contains))
                return AboKategorien.Streaming;
            if (SoftwareLizenzMerkmale.Any(normalized.Contains) || SoftwareLizenzGattungen.Any(normalized.Contains))
                return AboKategorien.SoftwareLizenz;
            foreach (var (category, markers) in KategorieMerkmale)
            {
                if (markers.Any(normalized.Contains))
                    return category;
            }
            return AboKategorien.Sonstige;
        }

        private static string RichtungErmitteln(
            IReadOnlyList<Transaktion> cluster,
            Func<int, bool>? istEinnahmenKonto)
        {
            var richtungen = cluster.Select(t =>
            {
                if (t.NachKontoId.HasValue)
                    return istEinnahmenKonto?.Invoke(t.NachKontoId.Value) == true
                        ? Zahlungsrichtungen.Einnahme
                        : Zahlungsrichtungen.Ausgabe;
                if (t.VonKontoId.HasValue || t.GeldinstitutId.HasValue)
                    return Zahlungsrichtungen.Einnahme;
                return Zahlungsrichtungen.Unklar;
            }).Where(value => value != Zahlungsrichtungen.Unklar).ToList();

            return richtungen.Count == 0
                ? Zahlungsrichtungen.Unklar
                : richtungen.GroupBy(value => value).OrderByDescending(group => group.Count()).First().Key;
        }

        private static bool IstIgnoriert(
            AboKandidat kandidat,
            IReadOnlyCollection<AboKandidatAusschluss>? ignorierteKandidaten)
        {
            if (ignorierteKandidaten == null || ignorierteKandidaten.Count == 0)
                return false;

            return ignorierteKandidaten.Any(ausschluss =>
            {
                if (ausschluss.AdresseId != kandidat.AdresseId)
                    return false;

                // Preisänderungen bis 20 % gehören weiterhin zum einmal abgewählten Muster.
                // Ein deutlich anderer Betrag kann dagegen ein wirklich neues Abo desselben
                // Anbieters sein und darf erneut vorgeschlagen werden.
                var referenz = Math.Max(Math.Abs(ausschluss.ReferenzBetrag), Math.Abs(kandidat.MedianBetrag));
                var toleranz = Math.Max(1m, referenz * 0.20m);
                return Math.Abs(ausschluss.ReferenzBetrag - kandidat.MedianBetrag) <= toleranz;
            });
        }

        private static AboKandidat BaueSemantischenKandidaten(
            List<Transaktion> cluster,
            string grund,
            string kategorie,
            string richtung)
        {
            var sortiert = cluster.OrderBy(t => t.Datum).ToList();
            var vermutetePeriode = ErmittlePeriodeAusWenigenZahlungen(sortiert);
            if (vermutetePeriode != null)
            {
                var kandidat = BaueKandidat(sortiert, vermutetePeriode);
                kandidat.Kategorie = kategorie;
                kandidat.Richtung = richtung;
                kandidat.Erkennungsgrund = $"{grund}; Rhythmus aus zwei Zahlungen plausibel";
                return kandidat;
            }

            // Ohne bestätigten Rhythmus nur die jüngste verdächtige Zahlung zuordnen.
            // Der Vorschlag bleibt bewusst abgewählt und verwendet erst nach ausdrücklicher
            // Wahl des Benutzers den vorläufigen Monatsrhythmus.
            var letzte = sortiert[^1];
            var einzelKandidat = BaueKandidat(new List<Transaktion> { letzte }, AboPerioden.Monatlich);
            einzelKandidat.Kategorie = kategorie;
            einzelKandidat.Richtung = richtung;
            einzelKandidat.Erkennungsgrund = $"{grund}; Rhythmus noch nicht bestätigt (vorläufig monatlich)";
            einzelKandidat.RhythmusNurVermutet = true;
            einzelKandidat.Uebernehmen = false;
            return einzelKandidat;
        }

        private static string? ErmittlePeriodeAusWenigenZahlungen(List<Transaktion> sortiert)
        {
            if (sortiert.Count < 2)
                return null;

            var abstaende = new List<int>();
            for (var i = 1; i < sortiert.Count; i++)
            {
                var tage = (int)(sortiert[i].Datum.Date - sortiert[i - 1].Datum.Date).TotalDays;
                if (tage > 0) abstaende.Add(tage);
            }

            if (abstaende.Count == 0)
                return null;

            var medianAbstand = (int)Median(abstaende.Select(value => (decimal)value).ToList());
            foreach (var (code, min, max, _) in Perioden)
            {
                if (medianAbstand < min || medianAbstand > max)
                    continue;

                var passend = abstaende.Count(value => value >= min && value <= max);
                if (passend * 2 >= abstaende.Count)
                    return code;
            }

            return null;
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

                return BaueKandidat(sortiert, code);
            }

            return null;
        }

        private static AboKandidat BaueKandidat(List<Transaktion> sortiert, string periodizitaet)
        {
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
                Periodizitaet = periodizitaet,
                MedianBetrag = Median(sortiert.Select(t => Math.Abs(t.Betrag)).ToList()),
                AnzahlZahlungen = sortiert.Count,
                ErsteZahlung = sortiert[0].Datum.Date,
                LetzteZahlung = sortiert[^1].Datum.Date,
                HaeufigstesKontoId = haeufigstesKonto,
                MehrereKonten = kontoIds.Distinct().Count() > 1,
                TransaktionIds = sortiert.Select(t => t.Id).ToList()
            };
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
