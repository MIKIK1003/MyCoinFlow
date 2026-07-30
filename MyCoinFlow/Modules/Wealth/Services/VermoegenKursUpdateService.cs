using MyCoinFlow.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyCoinFlow.Services
{
    public class VermoegenKursUpdateService
    {
        private readonly DatabaseService _db;
        private readonly KursService _kursService;

        public VermoegenKursUpdateService()
        {
            _db = new DatabaseService();
            _kursService = new KursService();
        }

        public async Task<VermoegenKursUpdateResult> AktualisierenAsync()
        {
            var result = new VermoegenKursUpdateResult();

            _db.EnsureVermoegenSchema();

            var einstellung = _db.VermoegenApiEinstellungGet();

            if (!einstellung.Aktiv || string.IsNullOrWhiteSpace(einstellung.ApiKey))
            {
                result.Meldung = "Kein aktiver EODHD API-Key vorhanden.";
                return result;
            }

            var positionen = _db.VermoegenPositionenGetAll()
                .Where(p => p.IstAktiv)
                .ToList();

            if (positionen.Count == 0)
            {
                result.Meldung = "Keine aktiven Vermögenspositionen vorhanden.";
                return result;
            }

            foreach (var p in positionen)
            {
                if (!string.IsNullOrWhiteSpace(p.Symbol))
                {
                    await KursluueckenNachholenAsync(p, einstellung.ApiKey, result);

                    var kurs = await _kursService.HoleAktuellenKursAsync(
                        p.Symbol,
                        p.Boerse,
                        einstellung.ApiKey);

                    if (kurs != null)
                    {
                        _db.VermoegenPositionKursUpdate(
                            p.Id,
                            kurs.Kurs,
                            kurs.KursDatum);

                        _db.VermoegenKursHistorieInsertIfMissing(
                            p.Id,
                            kurs.KursDatum,
                            kurs.Kurs,
                            "EODHD");

                        result.PositionenAktualisiert++;
                        continue;
                    }
                }

                var letzterKurs = _db.VermoegenKursHistorieGetLatestByPosition(p.Id);

                if (letzterKurs == null)
                {
                    result.PositionenOhneKursbasis++;
                    continue;
                }

                var fortschreibDatum = DateTime.Today;

                _db.VermoegenPositionKursUpdate(
                    p.Id,
                    letzterKurs.Kurs,
                    fortschreibDatum);

                _db.VermoegenKursHistorieInsertIfMissing(
                    p.Id,
                    fortschreibDatum,
                    letzterKurs.Kurs,
                    "Fortschreibung");

                result.PositionenAktualisiert++;
            }

            // Handels- und Einstandswährungen berücksichtigen (können pro Position abweichen).
            var fremdwaehrungen = positionen
                .SelectMany(p => new[]
                {
                    string.IsNullOrWhiteSpace(p.Waehrung) ? "CHF" : p.Waehrung.Trim().ToUpperInvariant(),
                    p.EffektiveEinstandWaehrung
                })
                .Where(w => w != "CHF")
                .Distinct()
                .ToList();

            foreach (var waehrung in fremdwaehrungen)
            {
                await FxLueckenNachholenAsync(waehrung, positionen, einstellung.ApiKey, result);

                var fx = await _kursService.HoleFxKursAsync(
                    waehrung,
                    "CHF",
                    einstellung.ApiKey);

                if (fx == null)
                {
                    result.FxOhneErgebnis++;
                    continue;
                }

                _db.VermoegenFxHistorieInsertIfMissing(
                    fx.VonWaehrung,
                    fx.NachWaehrung,
                    fx.KursDatum,
                    fx.Kurs,
                    "EODHD");

                result.FxGespeichert++;
            }

            result.Meldung =
                $"Kursaktualisierung abgeschlossen: " +
                $"{result.PositionenAktualisiert} Position(en) aktualisiert oder fortgeschrieben, " +
                $"{result.PositionenOhneKursbasis} ohne Kursbasis. " +
                $"FX: {result.FxGespeichert} gespeichert, {result.FxOhneErgebnis} ohne Ergebnis. " +
                $"Nachgeholt: {result.KurseNachgeholt} Kurs(e), {result.FxNachgeholt} FX-Kurs(e).";

            return result;
        }

        // Maximal so weit zurück, wie der EODHD-Free-Plan historische Daten liefert.
        private static readonly TimeSpan MaxRueckblick = TimeSpan.FromDays(365);

        // Holt fehlende Tagesschlusskurse nach. Der Backfill-Status merkt sich pro Position,
        // bis zu welchem Datum die Historie bereits vervollständigt wurde. Beim ersten Lauf
        // wird deshalb der komplette Bereich (ab Einstanddatum, max. 1 Jahr) geholt und
        // damit auch alle Lücken ZWISCHEN bestehenden Einträgen gefüllt.
        // Ein API-Aufruf pro Position, unabhängig von der Anzahl fehlender Tage.
        private async Task KursluueckenNachholenAsync(
            VermoegenPosition p,
            string apiKey,
            VermoegenKursUpdateResult result)
        {
            try
            {
                var fruehestesVon = DateTime.Today.Add(-MaxRueckblick);

                var abgedecktBis = _db.VermoegenBackfillStatusGet("KURS", p.Id.ToString());

                var von = abgedecktBis.HasValue
                    ? abgedecktBis.Value.AddDays(1)
                    : (p.EinstandDatum?.Date ?? fruehestesVon);

                if (von < fruehestesVon)
                    von = fruehestesVon;

                // Heute ist noch nicht abgeschlossen; den aktuellsten Kurs liefert der Realtime-Abruf.
                if (von >= DateTime.Today)
                    return;

                var tage = await _kursService.HoleEodHistorieAsync(
                    p.Symbol,
                    p.Boerse,
                    apiKey,
                    von,
                    DateTime.Today);

                if (tage.Count == 0)
                    return; // Fehler oder keine Daten -> beim nächsten Lauf erneut versuchen.

                var vorhandeneTage = _db.VermoegenKursHistorieGetByPosition(p.Id)
                    .Select(h => h.KursDatum.Date)
                    .ToHashSet();

                foreach (var tag in tage.Where(t => !vorhandeneTage.Contains(t.Datum)))
                {
                    _db.VermoegenKursHistorieInsertIfMissing(
                        p.Id,
                        tag.Datum,
                        tag.Kurs,
                        "EODHD-Historie");

                    result.KurseNachgeholt++;
                }

                _db.VermoegenBackfillStatusSet("KURS", p.Id.ToString(), DateTime.Today);
            }
            catch
            {
                // Backfill darf die normale Aktualisierung nie blockieren.
            }
        }

        // Holt fehlende FX-Tageskurse (Währung -> CHF) nach, damit auch für
        // nachgeholte Aktienkurse eine CHF-Umrechnung zum jeweiligen Datum vorliegt.
        // Auch hier merkt sich der Backfill-Status, bis wann bereits vervollständigt wurde.
        private async Task FxLueckenNachholenAsync(
            string waehrung,
            System.Collections.Generic.List<VermoegenPosition> positionen,
            string apiKey,
            VermoegenKursUpdateResult result)
        {
            try
            {
                var fruehestesVon = DateTime.Today.Add(-MaxRueckblick);

                var abgedecktBis = _db.VermoegenBackfillStatusGet("FX", waehrung);

                DateTime von;

                if (abgedecktBis.HasValue)
                {
                    von = abgedecktBis.Value.AddDays(1);
                }
                else
                {
                    // Erster Lauf: ab dem frühesten Kursdatum der Positionen in dieser
                    // Währung starten, damit die CHF-Spalten rückwirkend füllbar sind.
                    var fruehesterKurs = positionen
                        .Where(p =>
                            string.Equals(p.Waehrung?.Trim(), waehrung, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.EffektiveEinstandWaehrung, waehrung, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(p => _db.VermoegenKursHistorieGetByPosition(p.Id))
                        .Select(h => (DateTime?)h.KursDatum.Date)
                        .DefaultIfEmpty(null)
                        .Min();

                    von = fruehesterKurs ?? fruehestesVon;
                }

                if (von < fruehestesVon)
                    von = fruehestesVon;

                if (von >= DateTime.Today)
                    return;

                var tage = await _kursService.HoleFxHistorieAsync(
                    waehrung,
                    "CHF",
                    apiKey,
                    von,
                    DateTime.Today);

                if (tage.Count == 0)
                    return; // Fehler, keine Daten oder vom Free-Plan nicht unterstützt.

                var vorhandeneTage = _db.VermoegenFxHistorieGetNachChf(waehrung)
                    .Select(f => f.KursDatum.Date)
                    .ToHashSet();

                foreach (var tag in tage.Where(t => !vorhandeneTage.Contains(t.Datum)))
                {
                    _db.VermoegenFxHistorieInsertIfMissing(
                        waehrung,
                        "CHF",
                        tag.Datum,
                        tag.Kurs,
                        "EODHD-Historie");

                    result.FxNachgeholt++;
                }

                _db.VermoegenBackfillStatusSet("FX", waehrung, DateTime.Today);
            }
            catch
            {
                // Backfill darf die normale Aktualisierung nie blockieren.
            }
        }
    }

    public class VermoegenKursUpdateResult
    {
        public int PositionenAktualisiert { get; set; }
        public int PositionenOhneKursbasis { get; set; }
        public int FxGespeichert { get; set; }
        public int FxOhneErgebnis { get; set; }
        public int KurseNachgeholt { get; set; }
        public int FxNachgeholt { get; set; }

        public string Meldung { get; set; } = "";
    }
}