using System;

namespace MyCoinFlow.Models
{
    public class StweEinheitEigentumRow
    {
        public int Id { get; set; }
        public int EinheitId { get; set; }
        public int EigentuemerId { get; set; }

        public string EigentuemerName { get; set; } = "";

        public DateTime GueltigVon { get; set; }
        public DateTime? GueltigBis { get; set; }
    }
}
