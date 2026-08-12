using System;
using System.Collections.Generic;

namespace MyCoinFlow.Models
{
    public enum TransactionReportMode
    {
        SollIstMitHochrechnung,
        IstMitHochrechnung,
        NurBudget
    }

    public enum TransactionReportGrouping
    {
        Einzelkonto,
        Art,
        Gruppe,
        Untergruppe
    }

    public enum TransactionReportDirection
    {
        Ausgabe,
        Einnahme,
        Neutral
    }

    public sealed class TransactionReportAccount
    {
        public int KontoId { get; init; }
        public int Kontonummer { get; init; }
        public string Art { get; init; } = "";
        public string Gruppe { get; init; } = "";
        public string Untergruppe { get; init; } = "";
        public string Detail { get; init; } = "";
        public decimal? Jahresbudget { get; init; }
        public TransactionReportDirection Richtung { get; init; }
    }

    public sealed class TransactionReportOptions
    {
        public string Titel { get; init; } = "Transaktionsbericht";
        public string BudgetzeitraumBezeichnung { get; init; } = "";
        public DateTime BudgetVon { get; init; }
        public DateTime BudgetBis { get; init; }
        public DateTime AuswertungVon { get; init; }
        public DateTime AuswertungBis { get; init; }
        public TransactionReportMode Modus { get; init; }
        public TransactionReportGrouping Gruppierung { get; init; }
    }

    public sealed class TransactionReportRow
    {
        public int? KontoId { get; init; }
        public string Konto { get; init; } = "";
        public string Bezeichnung { get; init; } = "";
        public string Richtung { get; init; } = "";
        public decimal? BudgetJahr { get; init; }
        public decimal? SollZeitraum { get; init; }
        public decimal? IstZeitraum { get; init; }
        public decimal? HochrechnungJahr { get; init; }
        public decimal? DeltaZeitraum { get; init; }
        public decimal? DeltaJahr { get; init; }
        public decimal? ErfuellungProzent { get; init; }
    }

    public sealed class TransactionReportDirectionSummary
    {
        public decimal? BudgetJahr { get; init; }
        public decimal? SollZeitraum { get; init; }
        public decimal? IstZeitraum { get; init; }
        public decimal? HochrechnungJahr { get; init; }
        public decimal? DeltaZeitraum { get; init; }
        public decimal? DeltaJahr { get; init; }
    }

    public sealed class TransactionReportSpotlightRow
    {
        public int Rang { get; init; }
        public int KontoId { get; init; }
        public string Konto { get; init; } = "";
        public string Bezeichnung { get; init; } = "";
        public decimal Betrag { get; init; }
        public decimal AnteilProzent { get; init; }
        public decimal? HochrechnungJahr { get; init; }
        public decimal? HochrechnungAnteilProzent { get; init; }
    }

    public sealed class BudgetwertAenderung
    {
        public int KontoId { get; init; }
        public decimal NeuerWert { get; init; }
    }

    public sealed class TransactionReportResult
    {
        public TransactionReportOptions Optionen { get; init; } = new();
        public IReadOnlyList<TransactionReportRow> Zeilen { get; init; } = Array.Empty<TransactionReportRow>();
        public IReadOnlyList<TransactionReportRow> EinzelkontoZeilen { get; init; } = Array.Empty<TransactionReportRow>();
        public IReadOnlyList<TransactionReportSpotlightRow> GroessteAusgaben { get; init; } = Array.Empty<TransactionReportSpotlightRow>();
        public IReadOnlyList<TransactionReportSpotlightRow> GroessteEinnahmen { get; init; } = Array.Empty<TransactionReportSpotlightRow>();
        public IReadOnlyList<TransactionReportRow> GroessteAbweichungen { get; init; } = Array.Empty<TransactionReportRow>();
        public TransactionReportDirectionSummary Einnahmen { get; init; } = new();
        public TransactionReportDirectionSummary Ausgaben { get; init; } = new();
        public int AusgewaehlteKonten { get; init; }
        public int KontenOhneBudget { get; init; }
        public int Auswertungstage { get; init; }
        public int Budgettage { get; init; }
        public decimal BudgetabdeckungProzent { get; init; }
        public DateTime ErstelltAm { get; init; } = DateTime.Now;
    }
}
