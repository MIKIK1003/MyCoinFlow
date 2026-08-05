# -*- coding: utf-8 -*-
r"""
Erzeugt demo_seed.sql: Pseudo-Daten fuer die Demo-Datenbank "MyCoinFlowDemo".

Zweck: Marketing-Screenshots ohne echte Zahlen. Alle Personen, Banken und
Betraege sind frei erfunden; Haendlernamen sind gaengige Schweizer Brands,
wie sie in jedem Haushaltsbuch vorkommen.

Aufruf:  python make_demo_seed.py   -> schreibt demo_seed.sql ins gleiche Verzeichnis
Danach:  sqlcmd -S .\SQLEXPRESS -E -d MyCoinFlowDemo -i demo_seed.sql

Das SQL bricht hart ab, wenn es nicht auf 'MyCoinFlowDemo' laeuft.
"""

import random
from datetime import date, timedelta
from pathlib import Path

random.seed(20260730)  # reproduzierbar

OUT = Path(__file__).with_name("demo_seed.sql")

DB_NAME = "MyCoinFlowDemo"
START = date(2025, 4, 1)
END = date(2026, 7, 30)

GI_PRIVAT = "Alpenblick Bank – Privatkonto"
GI_SPAR = "Alpenblick Bank – Sparkonto"
GI_GEMEINSCHAFT = "Kantonalbank Demo – Gemeinschaftskonto"
GI_KARTE = "VISA Karte (SwissCard Demo)"

# ---------------------------------------------------------------- Kontenplan
# (Kontonummer, Art, Gruppe, Untergruppe, Budget 2026 pro Jahr oder None)
KONTEN = [
    (1000, "Einnahmen", "Einkommen", "Lohn Anna", 82200),
    (1010, "Einnahmen", "Einkommen", "Lohn Marc", 59760),
    (1100, "Einnahmen", "Einkommen", "Kinderzulagen", 4800),
    (1200, "Einnahmen", "Einkommen", "Zinsen & Dividenden", 600),
    (1300, "Einnahmen", "Einkommen", "Diverse Einnahmen", 0),
    (2000, "Ausgaben", "Fixkosten", "Miete", 25800),
    (2010, "Ausgaben", "Fixkosten", "Krankenkasse", 10680),
    (2020, "Ausgaben", "Fixkosten", "Versicherungen", 1880),
    (2030, "Ausgaben", "Fixkosten", "Internet & Mobile", 1430),
    (2040, "Ausgaben", "Fixkosten", "Strom & Nebenkosten", 2400),
    (2050, "Ausgaben", "Fixkosten", "Steuern", 15600),
    (2060, "Ausgaben", "Fixkosten", "Gebühren & Serafe", 500),
    (2070, "Ausgaben", "Fixkosten", "Kita", 11400),
    (2100, "Ausgaben", "Unterhaltskosten", "Lebensmittel", 13200),
    (2110, "Ausgaben", "Unterhaltskosten", "Haushalt & Drogerie", 2400),
    (2120, "Ausgaben", "Unterhaltskosten", "Kleidung", 3000),
    (2130, "Ausgaben", "Unterhaltskosten", "Gesundheit & Apotheke", 2000),
    (2140, "Ausgaben", "Unterhaltskosten", "Coiffeur & Körperpflege", 1200),
    (2200, "Ausgaben", "LifeStylekosten", "Restaurants & Ausgang", 3600),
    (2210, "Ausgaben", "LifeStylekosten", "Freizeit & Sport", 2400),
    (2220, "Ausgaben", "LifeStylekosten", "Ferien", 6000),
    (2230, "Ausgaben", "LifeStylekosten", "Geschenke", 1500),
    (2240, "Ausgaben", "LifeStylekosten", "Streaming & Abos", 600),
    (2250, "Ausgaben", "LifeStylekosten", "Musikschule", 480),
    (2300, "Ausgaben", "Mobilität", "ÖV & SBB", 2400),
    (2310, "Ausgaben", "Mobilität", "Treibstoff", 2160),
    (2320, "Ausgaben", "Mobilität", "Auto Unterhalt & Versicherung", 2800),
    (2400, "Ausgaben", "Finanzkosten", "Bankspesen", 240),
    (2410, "Ausgaben", "Finanzkosten", "Kreditkartengebühren", 100),
    (3000, "Investitionen", "Investitionen", "Möbel & Einrichtung", 4000),
    (3100, "Investitionen", "Investitionen", "Elektronik", 2500),
    (3200, "Investitionen", "Investitionen", "Velo & E-Bike", 3500),
    (5000, "Durchlaufkonten", "Durchlaufkonten", "Kreditkarten-Ausgleich", None),
    (5100, "Durchlaufkonten", "Durchlaufkonten", "Übertrag Sparen", None),
    (5200, "Durchlaufkonten", "Durchlaufkonten", "Übertrag Gemeinschaftskonto", None),
]

# Adressen: (Name, Typ, DefaultKonto oder None)
ADRESSEN = [
    ("Muster & Partner AG", "Arbeitgeber", 1000),
    ("Bergwerk Software GmbH", "Arbeitgeber", 1010),
    ("Ausgleichskasse Demo", "Amt", 1100),
    ("Immoverwaltung Seeblick AG", "Firma", 2000),
    ("VitaCare Krankenkasse", "Versicherung", 2010),
    ("Alpina Versicherungen AG", "Versicherung", 2020),
    ("Swisscom", "Firma", 2030),
    ("Elektra Regionalwerk", "Firma", 2040),
    ("Steueramt Kanton Zürich", "Amt", 2050),
    ("Serafe AG", "Amt", 2060),
    ("Gemeinde Musterhausen", "Amt", 2060),
    ("Kita Sunneschii", "Firma", 2070),
    ("Migros", "Detailhandel", 2100),
    ("Coop", "Detailhandel", 2100),
    ("Denner", "Detailhandel", 2100),
    ("Lidl Schweiz", "Detailhandel", 2100),
    ("Bäckerei Vogel", "Detailhandel", 2100),
    ("Coop City", "Detailhandel", 2110),
    ("Zalando", "Online-Shop", 2120),
    ("Apotheke am Markt", "Gesundheit", 2130),
    ("Dr. med. K. Steiner", "Gesundheit", 2130),
    ("Coiffeur Bellezza", "Dienstleistung", 2140),
    ("Restaurant Rössli", "Gastro", 2200),
    ("Pizzeria Da Enzo", "Gastro", 2200),
    ("Fitness Arena", "Freizeit", 2210),
    ("Hotel Bellavista Tessin", "Ferien", 2220),
    ("Bergbahnen Davos Demo", "Ferien", 2220),
    ("Netflix", "Abo", 2240),
    ("Spotify", "Abo", 2240),
    ("Musikschule Region Demo", "Bildung", 2250),
    ("SBB", "Mobilität", 2300),
    ("Avia Tankstelle", "Mobilität", 2310),
    ("Garage Weber AG", "Mobilität", 2320),
    ("TCS", "Mobilität", 2320),
    ("Möbel Rösch AG", "Detailhandel", 3000),
    ("Digitec Galaxus", "Online-Shop", 3100),
    ("Interdiscount", "Detailhandel", 3100),
    ("Veloplus", "Detailhandel", 3200),
    ("Alpenblick Bank AG", "Bank", None),
    ("Eigenübertrag", "Intern", None),
]

ALIASE = [
    ("Migros", "MIGROS M GENOSSENSCHAFT"),
    ("Migros", "MIGROLINO AG"),
    ("Coop", "COOP-3456 FILIALE"),
    ("SBB", "SBB CFF FFS"),
    ("Swisscom", "SWISSCOM (SCHWEIZ) AG"),
    ("VitaCare Krankenkasse", "VITACARE PRAEMIE KVG"),
]


def q(s: str) -> str:
    return s.replace("'", "''")


def d8(dt: date) -> str:
    return dt.strftime("%Y%m%d")


def chf(x: float) -> str:
    return f"{x:.2f}"


def rappen5(x: float) -> float:
    return round(round(x * 20) / 20, 2)


# ---------------------------------------------------------------- Transaktionen
TX = []  # (datum, von_kto, nach_kto, betrag, notiz, adresse, gi)


def tx(dt, von, nach, betrag, notiz, adr, gi):
    TX.append((dt, von, nach, round(betrag, 2), notiz, adr, gi))


def month_iter():
    y, m = START.year, START.month
    while (y, m) <= (END.year, END.month):
        yield y, m
        m += 1
        if m > 12:
            m, y = 1, y + 1


MONATE = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli",
          "August", "September", "Oktober", "November", "Dezember"]

LEBENSMITTEL = [("Migros", 0.5), ("Coop", 0.3), ("Denner", 0.1), ("Lidl Schweiz", 0.1)]

for y, m in month_iter():
    mn = MONATE[m - 1]

    def dom(dd):
        return date(y, m, min(dd, 28))

    # --- Einkommen (25.)
    tx(dom(25), None, 1000, 6850.00, f"Lohn {mn} {y}", "Muster & Partner AG", GI_PRIVAT)
    tx(dom(25), None, 1010, 4980.00, f"Lohn {mn} {y}", "Bergwerk Software GmbH", GI_PRIVAT)
    tx(dom(25), None, 1100, 400.00, f"Kinderzulagen {mn}", "Ausgleichskasse Demo", GI_PRIVAT)

    # --- Überträge
    tx(dom(26), None, 5200, 3400.00, "Haushaltsgeld Gemeinschaftskonto", "Eigenübertrag", GI_PRIVAT)
    tx(dom(26), 5200, None, 3400.00, "Haushaltsgeld Gemeinschaftskonto", "Eigenübertrag", GI_GEMEINSCHAFT)
    tx(dom(26), None, 5100, 800.00, "Sparen", "Eigenübertrag", GI_PRIVAT)
    tx(dom(26), 5100, None, 800.00, "Sparen", "Eigenübertrag", GI_SPAR)

    # --- Fixkosten
    tx(dom(1), None, 2000, 2150.00, f"Miete {mn}", "Immoverwaltung Seeblick AG", GI_GEMEINSCHAFT)
    tx(dom(3), None, 2010, 890.40, f"Prämie {mn}", "VitaCare Krankenkasse", GI_PRIVAT)
    tx(dom(5), None, 2070, 950.00, f"Kita {mn}", "Kita Sunneschii", GI_GEMEINSCHAFT)
    tx(dom(10), None, 2030, 118.90, "Internet & Mobile", "Swisscom", GI_PRIVAT)
    tx(dom(10), None, 2050, 1300.00, f"Steuerrate {mn}", "Steueramt Kanton Zürich", GI_PRIVAT)
    tx(dom(28), None, 2400, 5.00, "Kontoführung", "Alpenblick Bank AG", GI_PRIVAT)

    if m in (1, 4, 7, 10):
        tx(dom(15), None, 2040, 585.50, "Strom Quartalsrechnung", "Elektra Regionalwerk", GI_GEMEINSCHAFT)
        tx(dom(18), None, 2020, 468.60, "Quartalsprämie Hausrat/Haftpflicht", "Alpina Versicherungen AG", GI_PRIVAT)
        tx(dom(20), None, 2250, 120.00, "Musikschule Quartal", "Musikschule Region Demo", GI_PRIVAT)
        zins = rappen5(random.uniform(38, 72))
        tx(dom(28), None, 1200, zins, "Zinsgutschrift Sparkonto", "Alpenblick Bank AG", GI_SPAR)

    if m == 2:
        tx(dom(8), None, 2060, 335.00, "Serafe Jahresrechnung", "Serafe AG", GI_GEMEINSCHAFT)
    if m == 5:
        tx(dom(12), None, 2060, 128.00, "Abfallgebühren", "Gemeinde Musterhausen", GI_GEMEINSCHAFT)
    if m == 1 and y == 2026:
        tx(dom(15), None, 2410, 100.00, "Jahresgebühr Kreditkarte", "Alpenblick Bank AG", GI_KARTE)

    # --- Lebensmittel (9-12 Einkäufe)
    for _ in range(random.randint(9, 12)):
        shop = random.choices([s for s, _ in LEBENSMITTEL], weights=[w for _, w in LEBENSMITTEL])[0]
        betrag = rappen5(random.uniform(18, 155))
        notiz = random.choice(["Wocheneinkauf", "Einkauf", "Grosseinkauf", "Znüni & Früchte", ""])
        tx(dom(random.randint(1, 28)), None, 2100, betrag, notiz, shop, GI_GEMEINSCHAFT)
    for _ in range(random.randint(1, 3)):
        tx(dom(random.randint(1, 28)), None, 2100, rappen5(random.uniform(6, 19)), "Brot & Gipfeli", "Bäckerei Vogel", GI_GEMEINSCHAFT)

    # --- Haushalt & Drogerie
    for _ in range(2):
        tx(dom(random.randint(2, 27)), None, 2110, rappen5(random.uniform(22, 95)), "Drogerie & Haushalt", "Coop City", GI_GEMEINSCHAFT)

    # --- Gastro
    for _ in range(random.randint(2, 3)):
        lokal = random.choice(["Restaurant Rössli", "Pizzeria Da Enzo"])
        tx(dom(random.randint(3, 28)), None, 2200, rappen5(random.uniform(42, 145)), "Essen auswärts", lokal, GI_PRIVAT)

    # --- Mobilität
    tx(dom(2), None, 2300, 89.00, "ÖV-Monatsabo", "SBB", GI_PRIVAT)
    tx(dom(random.randint(8, 20)), None, 2300, rappen5(random.uniform(12, 46)), "Einzelbillette", "SBB", GI_PRIVAT)
    for _ in range(2):
        tx(dom(random.randint(4, 26)), None, 2310, rappen5(random.uniform(68, 96)), "Tanken", "Avia Tankstelle", GI_PRIVAT)

    # --- Karte: Streaming & Online
    tx(dom(4), None, 2240, 19.90, "Netflix Abo", "Netflix", GI_KARTE)
    tx(dom(7), None, 2240, 12.95, "Spotify Abo", "Spotify", GI_KARTE)
    if random.random() < 0.7:
        tx(dom(random.randint(5, 25)), None, 2120, rappen5(random.uniform(55, 180)), "Bestellung", "Zalando", GI_KARTE)

    # --- Freizeit / Gesundheit / Pflege
    tx(dom(6), None, 2210, 79.00, "Fitness-Abo", "Fitness Arena", GI_PRIVAT)
    if random.random() < 0.55:
        tx(dom(random.randint(3, 27)), None, 2130, rappen5(random.uniform(24, 78)), "Apotheke", "Apotheke am Markt", GI_PRIVAT)
    if m in (2, 6, 10):
        tx(dom(random.randint(8, 22)), None, 2130, rappen5(random.uniform(140, 260)), "Arztrechnung", "Dr. med. K. Steiner", GI_PRIVAT)
    if m % 2 == 0:
        tx(dom(random.randint(5, 25)), None, 2140, 85.00, "Coiffeur", "Coiffeur Bellezza", GI_PRIVAT)
    if random.random() < 0.4:
        tx(dom(random.randint(2, 26)), None, 2230, rappen5(random.uniform(28, 120)), "Geschenk", random.choice(["Migros", "Digitec Galaxus", "Coop City"]), GI_PRIVAT)

    # --- Kreditkarten-Ausgleich (27.)
    tx(dom(27), None, 5000, 450.00, "Kreditkarten-Abrechnung", "Eigenübertrag", GI_PRIVAT)
    tx(dom(27), 5000, None, 450.00, "Kreditkarten-Abrechnung", "Eigenübertrag", GI_KARTE)

# --- Einmalige Ereignisse
tx(date(2025, 7, 14), None, 2220, 2850.00, "Sommerferien Tessin, 1 Woche", "Hotel Bellavista Tessin", GI_PRIVAT)
tx(date(2025, 7, 18), None, 2220, 240.00, "Ausflüge Ferien", "Hotel Bellavista Tessin", GI_PRIVAT)
tx(date(2026, 2, 9), None, 2220, 1950.00, "Skiferien Davos", "Bergbahnen Davos Demo", GI_PRIVAT)
tx(date(2026, 7, 6), None, 2220, 2400.00, "Sommerferien Tessin", "Hotel Bellavista Tessin", GI_PRIVAT)
tx(date(2025, 9, 20), None, 3000, 1450.00, "Neues Sofa", "Möbel Rösch AG", GI_PRIVAT)
tx(date(2025, 11, 28), None, 3100, 899.00, "Fernseher (Black Friday)", "Digitec Galaxus", GI_KARTE)
tx(date(2026, 5, 16), None, 3100, 349.00, "Kaffeemaschine Ersatz", "Interdiscount", GI_PRIVAT)
tx(date(2026, 4, 11), None, 3200, 3200.00, "E-Bike", "Veloplus", GI_PRIVAT)
tx(date(2025, 10, 8), None, 2320, 780.00, "Service & Reifen", "Garage Weber AG", GI_PRIVAT)
tx(date(2026, 3, 14), None, 2320, 620.00, "Autoversicherung Jahresprämie", "TCS", GI_PRIVAT)
# Rückerstattungen (Von-Seite = Ausgabenkonto -> Bank +)
tx(date(2025, 8, 21), 2130, None, 186.40, "Rückerstattung Franchise", "VitaCare Krankenkasse", GI_PRIVAT)
tx(date(2026, 3, 30), 2130, None, 224.15, "Rückerstattung Arztkosten", "VitaCare Krankenkasse", GI_PRIVAT)

TX = [t for t in TX if START <= t[0] <= END]
TX.sort(key=lambda t: t[0])

# ---------------------------------------------------------------- Vermögen
DEPOT_WS = "Wertschriftendepot Alpenblick"
DEPOT_3A = "Säule 3a Vorsorge"
POSITIONEN = [
    # (Depot, Titel, ISIN, Klasse, Anzahl, Einstand, EinstandDatum, Symbol, Währung, StartKurs, EndKurs)
    (DEPOT_WS, "iShares Core SPI ETF", "CH0237935652", "ETF", 85, 118.50, "20240603", "CHSPI", "CHF", 124.0, 142.6),
    (DEPOT_WS, "Vanguard FTSE All-World ETF", "IE00B3RBWM25", "ETF", 40, 98.20, "20240415", "VWRL", "USD", 104.0, 118.4),
    (DEPOT_WS, "Nestlé N", "CH0038863350", "Aktie", 25, 102.40, "20231110", "NESN", "CHF", 96.0, 88.6),
    (DEPOT_WS, "Roche GS", "CH0012032048", "Aktie", 12, 245.00, "20240220", "ROG", "CHF", 252.0, 278.4),
    (DEPOT_WS, "Swisscom N", "CH0008742519", "Aktie", 8, 512.00, "20230905", "SCMN", "CHF", 522.0, 548.0),
    (DEPOT_3A, "Vorsorgefonds Demo 75", None, "Fonds", 320, 21.30, "20230115", None, "CHF", 22.1, 24.85),
]


def random_walk(start, end, n):
    vals, cur = [], start
    for i in range(n):
        drift = (end - start) / n
        cur = max(0.5, cur + drift + random.uniform(-1, 1) * abs(start) * 0.012)
        vals.append(round(cur, 2))
    vals[-1] = round(end, 2)
    return vals


def mondays():
    dt = START
    while dt.weekday() != 0:
        dt += timedelta(days=1)
    while dt <= END:
        yield dt
        dt += timedelta(weeks=1)


WOCHEN = list(mondays())

KURSE = {}  # titel -> [(datum, kurs)]
for p in POSITIONEN:
    KURSE[p[1]] = list(zip(WOCHEN, random_walk(p[9], p[10], len(WOCHEN))))

FX = list(zip(WOCHEN, random_walk(0.885, 0.792, len(WOCHEN))))  # USD->CHF

# ---------------------------------------------------------------- SQL bauen
S = []
S.append(f"""-- =====================================================================
-- MyCoinFlow Demo-Seed  (generiert von make_demo_seed.py, Seed 20260730)
-- Nur fuer die Datenbank '{DB_NAME}'! Alle Daten sind frei erfunden.
-- =====================================================================
SET NOCOUNT ON;
IF DB_NAME() <> N'{DB_NAME}'
BEGIN
    RAISERROR(N'ABBRUCH: Dieses Skript darf nur auf {DB_NAME} laufen (aktuell: %s).', 16, 1, @@SERVERNAME) WITH NOWAIT;
    SET NOEXEC ON;
END
GO
BEGIN TRAN;
""")

# ---- Aufräumen (Reihenfolge wegen FKs; Modul-Tabellen dynamisch, da evtl. fehlend)
DYN_CLEAN = [
    "StweSetLine", "StweSet", "StweZaehlerdatenLine", "StweZaehlerdatenMonat", "StweZaehlerdatenSet",
    "StweZaehlerLine", "StweZaehler", "StweEnergieSetZaehler", "StweEnergieSetMeta",
    "StweSchluesselLine", "StweSchluessel", "StweEinheitEigentum", "StweEinheit",
    "StweEigentuemer", "StweLiegenschaft",
    "VermoegenKursHistorie", "VermoegenBackfillStatus", "VermoegenPosition", "VermoegenDepot",
    "VermoegenFxHistorie",
    "HaushaltAufgabe", "HaushaltObjekt", "HaushaltObjektKategorie", "HaushaltRaum",
    "HaushaltStandort", "HaushaltZeitintervall", "HaushaltArbeitsanweisung",
    "AdressBuchungsregelBeleg", "AdressBuchungsregel", "KategorieStandardkonto", "KontoSchnellwahl",
    "AttachmentText", "Attachment",
    "BankImportItem", "BankImportItemArchive", "BankImportBatch",
    "CreditCardImportStaging", "CreditCardImportArchive", "CreditCardImportBatch",
    "KategorieKontoMapping",
]
S.append("-- Bestehende (Demo-)Daten entfernen")
for t in DYN_CLEAN:
    S.append(f"IF OBJECT_ID(N'dbo.{t}', N'U') IS NOT NULL EXEC(N'DELETE FROM dbo.{t}');")
S.append("""DELETE FROM Transaktion;
DELETE FROM BudgetDetail;
DELETE FROM Budgetzeitraum;
DELETE FROM AdresseAlias;
DELETE FROM Adresse;
DELETE FROM Geldinstitut;
DELETE FROM Kontenplan;
DELETE FROM KontenUnterGruppe;
DELETE FROM KontenGruppe;
DELETE FROM KontenArt;
""")

# ---- Lookups
arten = sorted({a for _, a, _, _, _ in KONTEN} | {"Amortisationen"})
gruppen = sorted({g for _, _, g, _, _ in KONTEN})
ugruppen = sorted({u for _, _, _, u, _ in KONTEN})
S.append("-- Kontenstruktur")
S.append("INSERT INTO KontenArt (Bezeichnung) VALUES " + ", ".join(f"(N'{q(a)}')" for a in arten) + ";")
S.append("INSERT INTO KontenGruppe (Bezeichnung) VALUES " + ", ".join(f"(N'{q(g)}')" for g in gruppen) + ";")
S.append("INSERT INTO KontenUnterGruppe (Bezeichnung) VALUES " + ", ".join(f"(N'{q(u)}')" for u in ugruppen) + ";")

rows = ", ".join(
    f"({nr}, N'{q(art)}', N'{q(grp)}', N'{q(ug)}', N'')"
    for nr, art, grp, ug, _ in KONTEN
)
S.append(f"INSERT INTO Kontenplan (Kontonummer, Art, Gruppe, Untergruppe, Detail) VALUES {rows};")

# ---- Geldinstitute
S.append(f"""-- Geldinstitute (fiktiv)
INSERT INTO Geldinstitut (Name, BIC, IBAN, KontoNummer, Notiz, Anfangsbestand, Anfangsdatum) VALUES
 (N'{q(GI_PRIVAT)}',        N'ALPBCH22XXX', N'CH12 0483 5012 3456 7100 0', N'100.123.456-01', N'Demo-Daten', 14250.00, CONVERT(date,'20250401')),
 (N'{q(GI_SPAR)}',          N'ALPBCH22XXX', N'CH12 0483 5012 3456 7200 0', N'100.123.456-02', N'Demo-Daten', 41800.00, CONVERT(date,'20250401')),
 (N'{q(GI_GEMEINSCHAFT)}',  N'KBDECH22XXX', N'CH45 0070 0110 0012 3456 7', N'20-44556-7',     N'Demo-Daten',  6300.00, CONVERT(date,'20250401')),
 (N'{q(GI_KARTE)}',         NULL,           NULL,                          N'**** **** **** 4711', N'Demo-Daten', 0.00, CONVERT(date,'20250401'));""")

# ---- Adressen
adr_rows = ", ".join(
    f"(N'{q(name)}', N'{q(typ)}', {konto if konto else 'NULL'})"
    for name, typ, konto in ADRESSEN
)
S.append(f"""-- Adressen (fiktiv bzw. gaengige Haendler)
INSERT INTO Adresse (Name, Typ, Land, IstBudgetiert, DefaultKontoId)
SELECT v.Name, v.Typ, N'Schweiz', 0, k.Id
FROM (VALUES {adr_rows}) v(Name, Typ, KontoNr)
LEFT JOIN Kontenplan k ON k.Kontonummer = v.KontoNr;""")

alias_rows = ", ".join(f"(N'{q(a)}', N'{q(al)}')" for a, al in ALIASE)
S.append(f"""INSERT INTO AdresseAlias (AdresseId, Text)
SELECT a.Id, v.Alias FROM (VALUES {alias_rows}) v(Name, Alias)
JOIN Adresse a ON a.Name = v.Name;""")

# ---- Budget
S.append("""-- Budget 2026 (aktiv) und 2025 (inaktiv)
INSERT INTO Budgetzeitraum (Bezeichnung, Startdatum, Enddatum, IstAktiv)
VALUES (N'Budget 2026', CONVERT(date,'20260101'), CONVERT(date,'20261231'), 1),
       (N'Budget 2025', CONVERT(date,'20250101'), CONVERT(date,'20251231'), 0);""")
for bez, faktor in (("Budget 2026", 1.0), ("Budget 2025", 0.96)):
    bud_rows = ", ".join(f"({nr}, {round(b * faktor)})" for nr, _, _, _, b in KONTEN if b)
    S.append(f"""INSERT INTO BudgetDetail (ZeitraumId, KontoId, Budgetwert)
SELECT (SELECT Id FROM Budgetzeitraum WHERE Bezeichnung = N'{bez}'), k.Id, v.Wert
FROM (VALUES {bud_rows}) v(KontoNr, Wert)
JOIN Kontenplan k ON k.Kontonummer = v.KontoNr;""")

# ---- Transaktionen (chunked)
S.append(f"-- Transaktionen: {len(TX)} Buchungen {START} bis {END}")
CHUNK = 350
for i in range(0, len(TX), CHUNK):
    chunk = TX[i:i + CHUNK]
    vals = ",\n ".join(
        f"(CONVERT(date,'{d8(dt)}'), {von if von else 'NULL'}, {nach if nach else 'NULL'}, "
        f"{chf(betrag)}, N'{q(notiz)}', N'{q(adr)}', N'{q(gi)}')"
        for dt, von, nach, betrag, notiz, adr, gi in chunk
    )
    S.append(f"""INSERT INTO Transaktion (Datum, VonKontoId, NachKontoId, Betrag, Notiz, AdresseId, GeldinstitutId)
SELECT v.Datum, kv.Id, kn.Id, v.Betrag, NULLIF(v.Notiz, N''), a.Id, g.Id
FROM (VALUES
 {vals}
) v(Datum, VonKto, NachKto, Betrag, Notiz, AdrName, GiName)
LEFT JOIN Kontenplan kv ON kv.Kontonummer = v.VonKto
LEFT JOIN Kontenplan kn ON kn.Kontonummer = v.NachKto
LEFT JOIN Adresse a ON a.Name = v.AdrName
LEFT JOIN Geldinstitut g ON g.Name = v.GiName;""")


# ---- Modul-Sektionen (dynamisch, falls Tabellen existieren)
def dyn(section_name: str, guard_table: str, inner_sql: str) -> str:
    esc = inner_sql.replace("'", "''")
    return (f"IF OBJECT_ID(N'dbo.{guard_table}', N'U') IS NOT NULL\n"
            f"    EXEC(N'{esc}');\nELSE\n"
            f"    PRINT N'>> Modul {section_name}: Tabellen fehlen noch - Modul in der App einmal oeffnen und Skript erneut ausfuehren.';")


# ---- Vermögen
verm = []
verm.append(f"""INSERT INTO VermoegenDepot (Name, Institut, Waehrung, IstAktiv, ErstelltAm, IstStandard) VALUES
 (N'{q(DEPOT_WS)}', N'Alpenblick Bank AG', N'CHF', 1, SYSDATETIME(), 1),
 (N'{q(DEPOT_3A)}', N'Alpenblick Bank AG', N'CHF', 1, SYSDATETIME(), 0);""")
for depot, titel, isin, klasse, anz, einstand, edat, symbol, whg, _, _ in POSITIONEN:
    letzter = KURSE[titel][-1]
    verm.append(f"""INSERT INTO VermoegenPosition (DepotId, Titel, ISIN, Anlageklasse, Anzahl, Einstandspreis, EinstandDatum, AktuellerKurs, KursDatum, IstAktiv, ErstelltAm, Symbol, Waehrung, EinstandWaehrung)
SELECT d.Id, N'{q(titel)}', {f"N'{isin}'" if isin else 'NULL'}, N'{klasse}', {anz}, {chf(einstand)}, CONVERT(date,'{edat}'), {chf(letzter[1])}, CONVERT(date,'{d8(letzter[0])}'), 1, SYSDATETIME(), {f"N'{symbol}'" if symbol else 'NULL'}, N'{whg}', N'{whg}'
FROM VermoegenDepot d WHERE d.Name = N'{q(depot)}';""")
    kurs_vals = ",\n  ".join(f"(CONVERT(date,'{d8(kd)}'), {chf(k)})" for kd, k in KURSE[titel])
    verm.append(f"""INSERT INTO VermoegenKursHistorie (PositionId, KursDatum, Kurs, Quelle, ErfasstAm)
SELECT p.Id, v.KursDatum, v.Kurs, N'Demo', SYSDATETIME()
FROM (VALUES
  {kurs_vals}
) v(KursDatum, Kurs)
CROSS JOIN (SELECT Id FROM VermoegenPosition WHERE Titel = N'{q(titel)}') p;""")
fx_vals = ",\n  ".join(f"(CONVERT(date,'{d8(kd)}'), {k:.4f})" for kd, k in FX)
verm.append(f"""INSERT INTO VermoegenFxHistorie (VonWaehrung, NachWaehrung, KursDatum, Kurs, Quelle, ErfasstAm)
SELECT N'USD', N'CHF', v.KursDatum, v.Kurs, N'Demo', SYSDATETIME()
FROM (VALUES
  {fx_vals}
) v(KursDatum, Kurs);""")
S.append(dyn("Vermögen", "VermoegenDepot", "\n".join(verm)))

# ---- Haushalt
hh = []
hh.append("""INSERT INTO HaushaltStandort (Bezeichnung, IconKey, FarbeKey, IstAktiv, ErstelltAm)
VALUES (N'Wohnung Musterweg 5', N'HomeCityOutline', N'DeepPurple', 1, SYSDATETIME());
INSERT INTO HaushaltRaum (Bezeichnung, IconKey, IstAktiv, ErstelltAm, StandortId)
SELECT v.B, N'HomeOutline', 1, SYSDATETIME(), s.Id
FROM (VALUES (N'Küche'), (N'Bad'), (N'Wohnzimmer'), (N'Keller'), (N'Balkon')) v(B)
CROSS JOIN (SELECT TOP 1 Id FROM HaushaltStandort) s;
INSERT INTO HaushaltObjektKategorie (Bezeichnung, IconKey, IstAktiv, ErstelltAm) VALUES
 (N'Elektrogeräte', N'PackageVariantClosed', 1, SYSDATETIME()),
 (N'Möbel', N'PackageVariantClosed', 1, SYSDATETIME());
INSERT INTO HaushaltZeitintervall (Bezeichnung, Tage, IstAktiv, ErstelltAm) VALUES
 (N'Monatlich', 30, 1, SYSDATETIME()), (N'Quartalsweise', 90, 1, SYSDATETIME()),
 (N'Halbjährlich', 180, 1, SYSDATETIME()), (N'Jährlich', 365, 1, SYSDATETIME());
INSERT INTO HaushaltArbeitsanweisung (Bezeichnung, Beschreibung, IconKey, IstAktiv, ErstelltAm) VALUES
 (N'Entkalken', N'Gerät gemäss Anleitung entkalken, danach 2x mit klarem Wasser spülen.', N'Tools', 1, SYSDATETIME()),
 (N'Filter reinigen', N'Filter ausbauen, unter fliessendem Wasser reinigen, trocknen lassen.', N'Tools', 1, SYSDATETIME()),
 (N'Sicherheitscheck', N'Kabel, Anschlüsse und Dichtungen prüfen.', N'ClipboardTextOutline', 1, SYSDATETIME());""")
OBJEKTE = [
    ("Kaffeemaschine Jura E8", "Küche", "Elektrogeräte", "Jura", "E8", "20231112", 1095.00, "Entkalken", "Quartalsweise"),
    ("Geschirrspüler Adora", "Küche", "Elektrogeräte", "V-ZUG", "Adora SL", "20220305", 2150.00, "Filter reinigen", "Halbjährlich"),
    ("Waschmaschine W1", "Keller", "Elektrogeräte", "Miele", "W1 Classic", "20210918", 1899.00, "Sicherheitscheck", "Jährlich"),
    ("Staubsauger V15", "Keller", "Elektrogeräte", "Dyson", "V15 Detect", "20240402", 649.00, "Filter reinigen", "Monatlich"),
    ("Sofa Wohnlandschaft", "Wohnzimmer", "Möbel", None, None, "20250920", 1450.00, None, None),
    ("Fernseher OLED 55", "Wohnzimmer", "Elektrogeräte", "LG", "OLED55C4", "20251128", 899.00, None, None),
]
for bez, raum, kat, herst, modell, kdat, preis, anw, intervall in OBJEKTE:
    anw_join = f"a.Bezeichnung = N'{q(anw)}'" if anw else "1 = 0"
    int_join = f"z.Bezeichnung = N'{q(intervall)}'" if intervall else "1 = 0"
    hh.append(f"""INSERT INTO HaushaltObjekt (RaumId, Bezeichnung, IconKey, Hersteller, Modell, Kaufdatum, Kaufpreis, IstAktiv, ErstelltAm, KategorieId, ArbeitsanweisungId, ZeitintervallId, VorlaufTage)
SELECT r.Id, N'{q(bez)}', N'PackageVariantClosed', {f"N'{q(herst)}'" if herst else 'NULL'}, {f"N'{q(modell)}'" if modell else 'NULL'}, CONVERT(date,'{kdat}'), {chf(preis)}, 1, SYSDATETIME(), k.Id, a.Id, z.Id, 7
FROM HaushaltRaum r
JOIN HaushaltObjektKategorie k ON k.Bezeichnung = N'{q(kat)}'
LEFT JOIN HaushaltArbeitsanweisung a ON {anw_join}
LEFT JOIN HaushaltZeitintervall z ON {int_join}
WHERE r.Bezeichnung = N'{q(raum)}';""")
AUFGABEN = [
    ("Kaffeemaschine Jura E8", "Entkalken fällig", "20260715", "20260812"),
    ("Staubsauger V15", "Filter reinigen", "20260720", "20260805"),
    ("Geschirrspüler Adora", "Filter reinigen", "20260601", "20260710"),
    ("Waschmaschine W1", "Jährlicher Sicherheitscheck", "20260901", "20260930"),
]
for obj, titel, aktiv_ab, faellig in AUFGABEN:
    hh.append(f"""INSERT INTO HaushaltAufgabe (ObjektId, Titel, Status, AktivAb, FaelligAm, IstAktiv, ErstelltAm)
SELECT o.Id, N'{q(titel)}', N'Offen', CONVERT(date,'{aktiv_ab}'), CONVERT(date,'{faellig}'), 1, SYSDATETIME()
FROM HaushaltObjekt o WHERE o.Bezeichnung = N'{q(obj)}';""")
S.append(dyn("Haushalt", "HaushaltStandort", "\n".join(hh)))

# ---- STWE
stwe = """INSERT INTO StweLiegenschaft (Name, Strasse, PLZ, Ort, Notiz, CreatedAtUtc)
VALUES (N'STWE Sonnenhalde 7', N'Sonnenhalde 7', N'8400', N'Winterthur', N'Demo-Liegenschaft', SYSUTCDATETIME());
INSERT INTO StweEigentuemer (Name, Email, CreatedAtUtc) VALUES
 (N'Anna & Marc Muster', N'muster@example.ch', SYSUTCDATETIME()),
 (N'Erika Brunner', N'brunner@example.ch', SYSUTCDATETIME()),
 (N'Peter & Silvia Gerber', N'gerber@example.ch', SYSUTCDATETIME());
INSERT INTO StweEinheit (LiegenschaftId, Bezeichnung, Typ, MeaPromille, FlaecheM2)
SELECT l.Id, v.Bez, N'Wohnung', v.Mea, v.Flaeche
FROM (VALUES (N'Whg EG links', 285.0, 78.0), (N'Whg EG rechts', 290.0, 81.0), (N'Whg 1. OG', 425.0, 112.0)) v(Bez, Mea, Flaeche)
CROSS JOIN (SELECT TOP 1 Id FROM StweLiegenschaft) l;
INSERT INTO StweEinheitEigentum (EinheitId, EigentuemerId, GueltigVon)
SELECT e.Id, o.Id, CONVERT(date,'20220501')
FROM (VALUES (N'Whg EG links', N'Erika Brunner'), (N'Whg EG rechts', N'Peter & Silvia Gerber'), (N'Whg 1. OG', N'Anna & Marc Muster')) v(Einheit, Eigentuemer)
JOIN StweEinheit e ON e.Bezeichnung = v.Einheit
JOIN StweEigentuemer o ON o.Name = v.Eigentuemer;"""
S.append(dyn("STWE", "StweLiegenschaft", stwe))

S.append("""COMMIT;
PRINT N'=====================================================';
PRINT N'Demo-Seed abgeschlossen.';
PRINT N'Transaktionen: ' + CAST((SELECT COUNT(*) FROM Transaktion) AS nvarchar(10));
PRINT N'Adressen:      ' + CAST((SELECT COUNT(*) FROM Adresse) AS nvarchar(10));
PRINT N'Konten:        ' + CAST((SELECT COUNT(*) FROM Kontenplan) AS nvarchar(10));
SET NOEXEC OFF;
""")

OUT.write_text("\n".join(S), encoding="utf-8-sig")
print(f"OK: {OUT} geschrieben ({len(TX)} Transaktionen, {sum(len(v) for v in KURSE.values())} Kurspunkte)")
