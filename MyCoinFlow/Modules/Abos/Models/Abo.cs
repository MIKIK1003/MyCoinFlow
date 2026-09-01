using System;

namespace MyCoinFlow.Models
{
    /// <summary>
    /// Wiederkehrende Zahlungsserie, z.B. Einnahme, Vertrag, Lizenz oder Streaming.
    /// Statuswerte: "Aktiv", "Gekuendigt", "Beendet".
    /// Periodizitaet: "Monatlich", "Quartalsweise", "Halbjaehrlich", "Jaehrlich".
    /// </summary>
    public class Abo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? AdresseId { get; set; }
        public string? AdresseName { get; set; }

        public string Periodizitaet { get; set; } = AboPerioden.Monatlich;

        /// <summary>Fachliche Zahlungsrichtung der Serie.</summary>
        public string Richtung { get; set; } = Zahlungsrichtungen.Unklar;

        /// <summary>
        /// Thematische Art der Zahlungsserie.
        /// </summary>
        public string Kategorie { get; set; } = AboKategorien.Pruefen;

        /// <summary>Vom Benutzer pflegbare Bezeichnung der Kategorie.</summary>
        public string? KategorieBezeichnung { get; set; }

        public decimal? ErwarteterBetrag { get; set; }
        public decimal BetragToleranzProzent { get; set; } = 10m;

        public string Status { get; set; } = AboStatus.Aktiv;
        public DateTime? GekuendigtAm { get; set; }
        public int? KuendigungsfristTage { get; set; }

        /// <summary>Gewünschtes Vertragsende: "ab wann will ich das Abo nicht mehr".</summary>
        public DateTime? KuendigenZum { get; set; }

        /// <summary>
        /// Spätester Kündigungstermin: gewünschtes Ende minus Kündigungsfrist.
        /// Bis zu diesem Datum muss die Kündigung beim Anbieter sein.
        /// </summary>
        public DateTime? SpaetesterKuendigungsTermin =>
            KuendigenZum.HasValue
                ? KuendigenZum.Value.Date.AddDays(-(KuendigungsfristTage ?? 0))
                : (DateTime?)null;

        /// <summary>Tage vor der nächsten erwarteten Zahlung, ab denen die Ampel "Zahlung steht an" zeigt.</summary>
        public int VorwarnTage { get; set; } = 7;

        public int? ErwartetesKontoId { get; set; }

        public string? WebseiteUrl { get; set; }

        /// <summary>Konkreter Kündigungsweg, z.B. Kundenkonto, App Store oder E-Mail.</summary>
        public string? Kuendigungsweg { get; set; }
        public string? Notiz { get; set; }
    }

    public static class AboKategorien
    {
        public const string Streaming = "Streaming";
        public const string SoftwareLizenz = "SoftwareLizenz";
        public const string Vertrag = "Vertrag";
        public const string Wohnen = "Wohnen";
        public const string Versicherung = "Versicherung";
        public const string Telekommunikation = "Telekommunikation";
        public const string Mitgliedschaft = "Mitgliedschaft";
        public const string Finanzierung = "Finanzierung";
        public const string SteuernGebuehren = "SteuernGebuehren";
        public const string VorsorgeSparen = "VorsorgeSparen";
        public const string Dienstleistung = "Dienstleistung";
        public const string Sonstige = "Sonstige";
        public const string Pruefen = "Pruefen";

        public static string Anzeige(string? kategorie) => kategorie switch
        {
            Streaming => "Streaming",
            SoftwareLizenz => "Lizenzen & Software",
            Vertrag => "Verträge",
            Wohnen => "Wohnen & Immobilien",
            Versicherung => "Versicherungen",
            Telekommunikation => "Telekommunikation & Internet",
            Mitgliedschaft => "Mitgliedschaften",
            Finanzierung => "Finanzierung & Kredite",
            SteuernGebuehren => "Steuern & Gebühren",
            VorsorgeSparen => "Sparen & Vorsorge",
            Dienstleistung => "Dienstleistungen",
            Sonstige => "Sonstige Serien",
            Pruefen or null or "" => "Noch nicht kategorisiert",
            _ => kategorie
        };

        public static bool IstZahlungsserie(string? kategorie) => !string.IsNullOrWhiteSpace(kategorie);

        // Kompatibilität für bestehende Aufrufer und Datenmigration.
        public static bool IstDigitalesAbo(string? kategorie) => IstZahlungsserie(kategorie);
    }

    /// <summary>Benutzerdefinierbare Kategorie für Zahlungsserien.</summary>
    public class AboKategorie
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Bezeichnung { get; set; } = "";
        public string Beschreibung { get; set; } = "";
        public string FarbeHex { get; set; } = "#5B2DA9";
        public int Sortierung { get; set; }
        public bool IstSystem { get; set; }
        public bool IstAktiv { get; set; } = true;
        public int AnzahlSerien { get; set; }
    }

    public static class Zahlungsrichtungen
    {
        public const string Einnahme = "Einnahme";
        public const string Ausgabe = "Ausgabe";
        public const string Unklar = "Unklar";

        public static string Anzeige(string? richtung) => richtung switch
        {
            Einnahme => "Einnahmen",
            Ausgabe => "Ausgaben",
            _ => "Richtung prüfen"
        };
    }

    public static class AboStatus
    {
        public const string Aktiv = "Aktiv";
        public const string Gekuendigt = "Gekuendigt";
        public const string Beendet = "Beendet";
    }

    public static class AboPerioden
    {
        public const string Monatlich = "Monatlich";
        public const string Quartalsweise = "Quartalsweise";
        public const string Halbjaehrlich = "Halbjaehrlich";
        public const string Jaehrlich = "Jaehrlich";

        /// <summary>Nominale Periodenlänge in Tagen (für die Berechnung der nächsten erwarteten Zahlung).</summary>
        public static int Tage(string? periodizitaet) => periodizitaet switch
        {
            Quartalsweise => 91,
            Halbjaehrlich => 182,
            Jaehrlich => 365,
            _ => 30
        };

        /// <summary>Anzeige-Text (mit Umlauten).</summary>
        public static string Anzeige(string? periodizitaet) => periodizitaet switch
        {
            Quartalsweise => "Quartalsweise",
            Halbjaehrlich => "Halbjährlich",
            Jaehrlich => "Jährlich",
            Monatlich => "Monatlich",
            _ => periodizitaet ?? ""
        };
    }

    /// <summary>
    /// Einem Abo zugeordnete Transaktion (Detailliste im Abo-Modul).
    /// </summary>
    public class AboZahlung
    {
        public int AboId { get; set; }
        public int TransaktionId { get; set; }
        public DateTime Datum { get; set; }
        public decimal Betrag { get; set; }
        public int? VonKontoId { get; set; }
        public int? NachKontoId { get; set; }
        public string? AdresseName { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }
        public bool ManuellZugeordnet { get; set; }

        /// <summary>
        /// Gehört fachlich zur Serie, ist aber keine erwartete Wiederholung.
        /// Die Zahlung zählt zum historischen Total, nicht zu Rhythmus und Jahreswert.
        /// </summary>
        public bool IstEinmalig { get; set; }

        /// <summary>Buchungskonto der Zahlung (Aufwandsseite; bei Bankimporten ist Von die Bank).</summary>
        public int? BuchungsKontoId => NachKontoId ?? VonKontoId;
    }

    /// <summary>
    /// Kandidat für eine Lücke in der Zahlungsreihe eines Abos
    /// (Transaktion, die eine fehlende wiederkehrende Zahlung sein könnte).
    /// </summary>
    public class AboLueckeKandidat
    {
        public DateTime ErwartetAm { get; set; }
        public int TransaktionId { get; set; }
        public DateTime Datum { get; set; }
        public decimal Betrag { get; set; }
        public string? AdresseName { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }

        /// <summary>Transaktion hat dieselbe Adresse wie das Abo (starkes Indiz).</summary>
        public bool AdressePasst { get; set; }

        /// <summary>Transaktion gehört bereits einer ANDEREN Adresse (starkes Gegen-Indiz).</summary>
        public bool AdresseKonflikt { get; set; }

        /// <summary>Erklärung, warum der Kandidat (nicht) passt – für die Anzeige im Dialog.</summary>
        public string? MatchInfo { get; set; }

        /// <summary>Interne Bewertung für die Sortierung (höher = besser).</summary>
        public int Punkte { get; set; }

        /// <summary>Vom Benutzer im Dialog an-/abwählbar; nur sichere Treffer sind vorselektiert.</summary>
        public bool Uebernehmen { get; set; }
    }

    /// <summary>
    /// Von der automatischen Erkennung vorgeschlagenes Abo (noch nicht gespeichert).
    /// </summary>
    public class AboKandidat
    {
        public int AdresseId { get; set; }
        public string AdresseName { get; set; } = "";
        public string Periodizitaet { get; set; } = AboPerioden.Monatlich;
        public string Richtung { get; set; } = Zahlungsrichtungen.Unklar;
        public string Kategorie { get; set; } = AboKategorien.Pruefen;
        public string KategorieAnzeige => AboKategorien.Anzeige(Kategorie);
        public decimal MedianBetrag { get; set; }
        public int AnzahlZahlungen { get; set; }
        public DateTime ErsteZahlung { get; set; }
        public DateTime LetzteZahlung { get; set; }
        public int? HaeufigstesKontoId { get; set; }
        public bool MehrereKonten { get; set; }

        /// <summary>Für den Benutzer sichtbare Begründung der Erkennung.</summary>
        public string Erkennungsgrund { get; set; } = "";

        /// <summary>
        /// Kennzeichnet einen semantischen Streaming-/App-Verdacht ohne ausreichend
        /// bestätigten Zahlungsrhythmus. Solche Treffer sind nicht vorselektiert.
        /// </summary>
        public bool RhythmusNurVermutet { get; set; }

        /// <summary>Die Adresse hat bereits ein Abo (Kandidat ist z.B. ein zweiter Vertrag beim gleichen Anbieter).</summary>
        public bool AdresseHatAbo { get; set; }
        public System.Collections.Generic.List<int> TransaktionIds { get; set; } = new();

        /// <summary>Vom Benutzer im Kandidaten-Dialog an-/abwählbar.</summary>
        public bool Uebernehmen { get; set; } = true;
    }

    /// <summary>Dauerhaft vom Benutzer ausgeschlossener Kandidat.</summary>
    public class AboKandidatAusschluss
    {
        public int AdresseId { get; set; }
        public string Periodizitaet { get; set; } = AboPerioden.Monatlich;
        public decimal ReferenzBetrag { get; set; }
    }
}
