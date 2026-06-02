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
        public string Anlageklasse { get; set; } = "Aktie";

        public decimal Anzahl { get; set; }
        public decimal Einstandspreis { get; set; }
        public DateTime? EinstandDatum { get; set; }

        public decimal? AktuellerKurs { get; set; }
        public DateTime? KursDatum { get; set; }

        public string Notiz { get; set; } = "";
        public bool IstAktiv { get; set; } = true;

        public decimal EinstandWert => Anzahl * Einstandspreis;
        public decimal? Marktwert => AktuellerKurs.HasValue ? Anzahl * AktuellerKurs.Value : null;
        public decimal? GewinnVerlust => Marktwert.HasValue ? Marktwert.Value - EinstandWert : null;
    }
}