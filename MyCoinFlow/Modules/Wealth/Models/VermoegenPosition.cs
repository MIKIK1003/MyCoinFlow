using System;

namespace MyCoinFlow.Models
{
    public class VermoegenPosition
    {
        public int Id { get; set; }
        public int DepotId { get; set; }
        public string DepotName { get; set; } = "";

        public string Titel { get; set; } = "";
        public string ISIN { get; set; } = "";
        public string Valor { get; set; } = "";

        public string Symbol { get; set; } = "";
        public string Boerse { get; set; } = "";
        public string Waehrung { get; set; } = "CHF";

        // Währung, in der der Einstand bezahlt wurde. Leer = gleich wie Handelswährung (Waehrung).
        // Beispiel: Aktie wird in USD gehandelt, wurde aber in CHF gekauft und im Depot in CHF eingebucht.
        public string EinstandWaehrung { get; set; } = "";

        public string Anlageklasse { get; set; } = "Aktie";

        public decimal Anzahl { get; set; }
        public decimal Einstandspreis { get; set; }
        public DateTime? EinstandDatum { get; set; }

        public decimal? AktuellerKurs { get; set; }
        public DateTime? KursDatum { get; set; }

        public string Notiz { get; set; } = "";
        public bool IstAktiv { get; set; } = true;

        // Effektive Einstandswährung: fällt auf die Handelswährung zurück, wenn nichts erfasst ist.
        public string EffektiveEinstandWaehrung =>
            string.IsNullOrWhiteSpace(EinstandWaehrung)
                ? (string.IsNullOrWhiteSpace(Waehrung) ? "CHF" : Waehrung.Trim().ToUpperInvariant())
                : EinstandWaehrung.Trim().ToUpperInvariant();

        public bool HatAbweichendeEinstandWaehrung =>
            !string.Equals(
                EffektiveEinstandWaehrung,
                string.IsNullOrWhiteSpace(Waehrung) ? "CHF" : Waehrung.Trim().ToUpperInvariant(),
                StringComparison.OrdinalIgnoreCase);

        // EinstandWert ist in EffektiveEinstandWaehrung, Marktwert in Waehrung (Handelswährung).
        public decimal EinstandWert => Anzahl * Einstandspreis;
        public decimal? Marktwert => AktuellerKurs.HasValue ? Anzahl * AktuellerKurs.Value : null;

        // Nur berechenbar, wenn Einstand und Marktwert in derselben Währung vorliegen.
        // Bei abweichender Einstandswährung erfolgt der Vergleich ausschliesslich in CHF (ViewModel).
        public decimal? GewinnVerlust =>
            HatAbweichendeEinstandWaehrung
                ? null
                : (Marktwert.HasValue ? Marktwert.Value - EinstandWert : null);
    }
}