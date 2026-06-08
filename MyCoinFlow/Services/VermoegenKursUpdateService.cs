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

            var fremdwaehrungen = positionen
                .Select(p => string.IsNullOrWhiteSpace(p.Waehrung) ? "CHF" : p.Waehrung.Trim().ToUpperInvariant())
                .Where(w => w != "CHF")
                .Distinct()
                .ToList();

            foreach (var waehrung in fremdwaehrungen)
            {
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
                $"FX: {result.FxGespeichert} gespeichert, {result.FxOhneErgebnis} ohne Ergebnis.";

            return result;
        }
    }

    public class VermoegenKursUpdateResult
    {
        public int PositionenAktualisiert { get; set; }
        public int PositionenOhneKursbasis { get; set; }
        public int FxGespeichert { get; set; }
        public int FxOhneErgebnis { get; set; }

        public string Meldung { get; set; } = "";
    }
}