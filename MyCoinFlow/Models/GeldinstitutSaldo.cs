using MyCoinFlow.Models;

public class GeldinstitutSaldo : Geldinstitut
{
    public decimal Gebucht { get; set; }       // Summe Transaktionen (ab Anfangsdatum bis Abgrenzungsdatum)
    public decimal Schlussaldo { get; set; }   // Anfangsbestand + Gebucht
}
