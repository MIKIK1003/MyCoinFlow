# DMS-Feature – portable Spezifikation (für Portierung nach ShopFlow / WinUI3)

> Zweck dieses Dokuments: Beschreibt die in MyCoinFlow (WPF) gebaute DMS-Funktionalität
> **framework-neutral**, damit eine spätere Entwicklungssession im ShopFlow-Repo sie umsetzen kann,
> ohne dass der fachliche Hintergrund erneut erklärt werden muss. Es ist **keine 1:1-Codevorlage**
> – WinUI3 hat andere Controls/Dialoge als WPF/MaterialDesignInXaml, daher muss die UI-Schicht
> neu gebaut werden. Die reine Logik (Textanalyse, Queue-Orchestrierung) ist grösstenteils
> direkt wiederverwendbar.

---

## 1. Fachlicher Kontext & Begriffs-Mapping

| MyCoinFlow (dieses Projekt) | ShopFlow (Ziel) | Hinweis |
|---|---|---|
| `Transaktion` (importierte Bankbuchung) | **Journal-Eintrag** (Debit/Kredit-Buchungssatz) | Wird lokal durch Kasseneinzug oder Rechnungsausstellung im Verkaufsmodul erzeugt – **keine** Bankbuchung. |
| Rechnung (einzige Belegart) | **Kassenbeleg** oder **Rechnung** | Zwei Belegarten statt einer – Matching/Umbenennung muss dies berücksichtigen (siehe Abschnitt 5). |
| `Adresse` (Adressbuch) | Kunde/Lieferant (Gegenpartei-Entität) | Exakte Struktur in ShopFlow noch zu klären (siehe Abschnitt 8). |
| DatabaseService (SQL Server, eigene DB) | ShopFlow-eigene Datenhaltung | Schema-Muster (generische Attachment-Tabelle) ist aber 1:1 übertragbar. |

**Wichtigste Abgrenzung:** In MyCoinFlow ist die Transaktion selbst die Bankbuchung – Matching
passiert direkt gegen Bankdaten. In ShopFlow passiert das **nicht so**: Bankeingänge und alle
Fibu-Vorgänge laufen ausschliesslich im gekauften Fremd-ERP (nur Fibu), das über eine API
angesteuert wird. Das DMS in ShopFlow matcht Dokumente ausschliesslich gegen **lokale
Journal-Einträge** (die wiederum später über die API offene Posten ans Fibu-System melden) –
**nicht** gegen Bankbewegungen. Diese Trennung ist zentral und darf beim Portieren nicht
verwischt werden.

---

## 2. Ziel des Features (Kurzfassung)

Ein konfigurierter **Arbeitsordner** wird laufend überwacht. Neu eintreffende Dokumente werden:
1. sequenziell verarbeitet (visuell sichtbar, Fortschrittsanzeige),
2. per OCR/Textlayer erfasst, falls nicht maschinenlesbar,
3. automatisch sinnvoll benannt (Datum + Inhalt, feste Zeichenlänge für einheitliche Struktur),
4. gegen offene Journal-Einträge gematcht (Betrag exakt, Datumsfenster, Gegenpartei-Abgleich),
5. bei eindeutigem Treffer automatisch verknüpft, bei mehreren Treffern fragt ein Dialog nach,
6. bei keinem Treffer bleibt das Dokument frei im DMS verfügbar – und kann **später erneut**
   automatisch gesucht werden (Journal-Eintrag existiert evtl. noch nicht beim Eintreffen).

Verknüpfte Journal-Einträge können vom Dokument wieder **entkoppelt** ("zurückgestellt", Datei
bleibt im DMS) statt nur gelöscht zu werden.

---

## 3. Architektur-Überblick (Komponenten)

### 3.1 Direkt wiederverwendbar (kein WPF/UI-Bezug, reines BCL/C#)

- **Textanalyse** (Datum/Titel/Betrag aus OCR-Text extrahieren) – reine String-/Regex-Logik,
  keine Abhängigkeiten. Referenz: `Services/DmsDocumentAnalyzer.cs`.
  - `ExtractDocumentDate`: erkennt `31.12.2025`, `2025-12-31` **und** ausgeschriebene Formate
    wie `3. März 2026` (deutsche Monatsnamen). Wichtig: viele Rechnungen schreiben das Datum
    ausgeschrieben – rein numerische Patterns reichen nicht.
  - `ExtractTitle`: **nicht** "erste Textzeile" verwenden (liefert oft die Adresse des
    Empfängers statt des Ausstellers, v. a. wenn das Firmenlogo nur Bild ist). Stattdessen:
    bekannte Gegenpartei aus dem eigenen Adressbuch im Text erkennen (siehe 3.3) und deren
    Namen als Titel nehmen; ohne Treffer den ursprünglichen Dateinamen behalten.
  - `ExtractAmountCandidates`: liefert **mehrere** Betragskandidaten gewichtet nach Nähe zu
    Schlüsselwörtern ("Total", "zu zahlen" etc.) – beim Matching alle der Reihe nach probieren,
    nicht nur den bestbewerteten (Layout-Heuristiken können danebenliegen).
  - Konstante Titel-Länge (z. B. 40 Zeichen) für einheitliche Dateinamensstruktur.

- **Warteschlangen-/Prozess-Orchestrierung**: `FileSystemWatcher` + `BlockingCollection<T>` +
  Consumer-Task auf eigenem Thread, damit **sequenziell** (nicht parallel) verarbeitet wird.
  Referenz: `Services/DmsWatcherService.cs`.
  - Generisches "Arbeitspaket" (DedupKey, DisplayName, Action) statt hartcodierter Dateipfade –
    dadurch läuft sowohl "neue Datei verarbeiten" als auch "Matching erneut versuchen" (siehe
    Abschnitt 6) über dieselbe Queue/Fortschrittsanzeige.
  - Wichtige Falle: `BlockingCollection.GetConsumingEnumerable()` blockiert nach jedem Element
    einfach weiter, bis `CompleteAdding()` aufgerufen wird (erst bei Stop). Der "Beschäftigt"-
    Status muss deshalb **pro verarbeitetem Element** zurückgesetzt werden, nicht erst nach der
    Schleife – sonst bleibt eine Fortschrittsanzeige für immer "aktiv".
  - Framework-Bezug, der ersetzt werden muss: `Application.Current.Dispatcher.Invoke(...)` zum
    Marshalling von Property-Changes auf den UI-Thread → in WinUI3 `DispatcherQueue.TryEnqueue(...)`.

- **OCR/Text-Extraktion**: PdfPig (Textlayer ohne OCR) + Tesseract-CLI-Aufruf (`Process.Start`)
  als Fallback für Scans/Bilder. Referenz: `Services/OcrService.cs`. Reines C#, portierbar,
  sofern PdfPig als NuGet-Paket auch in ShopFlow eingebunden wird und Tesseract als externes
  Tool (Pfad konfigurierbar) verfügbar ist.

### 3.2 Portierbar mit Anpassung (Datenzugriff)

- **Generisches Attachment-Schema**: Eine Tabelle für alle Dokumente, nicht pro Entität eine
  eigene. Empfehlung: gleiches Muster wie hier übernehmen:
  - `Attachment(Id, EntityType, EntityId, FileName, OriginalName, FolderRel, SizeBytes,
    ImportedAtUtc, OcrStatus, Titel, Kategorie)` – `EntityType`/`EntityId` generisch (z. B.
    `"JournalEintrag"` + Id), nicht hart auf eine Entität verdrahtet. So können später weitere
    Entitätstypen (Kunde, Lieferant, …) Dokumente bekommen, ohne Schema-Änderung.
  - `AttachmentText(AttachmentId, Text, Lang, ExtractedAtUtc)` – Volltextindex getrennt von
    Metadaten, damit Re-Matching den Text wiederverwenden kann, **ohne erneut OCR laufen zu
    lassen** (siehe Abschnitt 6).
  - `AppSetting(Key, Value)` – simple Key/Value-Tabelle für Pfade/Flags (Arbeitsordner,
    Ablageordner, Tesseract-Pfad, Watcher aktiv/inaktiv).

- **Matching-Query**: Betrag exakt (`ABS(Betrag) = ABS(@betrag)`), Datumsfenster um das
  erkannte Dokumentdatum (Empfehlung: -10/+60 Tage – Rechnungsdatum liegt meist vor dem
  Zahlungsdatum, daher asymmetrisch), Journal-Einträge ausschliessen, die bereits ein
  Attachment haben. In ShopFlow: differenzieren zwischen Kassenbeleg (Zahlung = sofort, enges
  Fenster reicht) und Rechnung (Zahlungsziel, weiteres Fenster nötig) – siehe Abschnitt 8.

### 3.3 Gegenpartei-Erkennung (optional, aber empfohlen)

Falls ShopFlow bereits eine Adress-/Kunden-/Lieferanten-Erkennung besitzt (Alias-Matching wie
`AdressErkennungService` hier, genutzt auch beim Bank-Import), diese wiederverwenden: Text
gegen bekannte Namen/Aliase abgleichen. Ergebnis dient zwei Zwecken:
1. **Titel-Vorschlag** beim Ablegen (statt Fliesstext).
2. **Verengung bei mehreren Matching-Kandidaten** (wenn Betrag+Datum mehrdeutig sind, aber nur
   ein Kandidat zur erkannten Gegenpartei passt).

Ohne vorhandene Erkennung: einfacher Fallback – Text auf direkte Vorkommen bekannter
Kunden-/Lieferantennamen durchsuchen (Substring-Vergleich, Mindestlänge, um Störtreffer zu
vermeiden).

---

## 4. Verzeichnis-/Namenslogik

- **Zwei Einstellungen** (Key/Value in `AppSetting`):
  - `DmsWorkingFolder` – überwachter Arbeitsordner (neue Dokumente werden hierher verschoben).
  - `AttachmentRoot` (o. ä.) – Ablageordner-Root, in den archiviert wird.
  - Zusätzlich: `DmsWatcherEnabled` (1/0), um Überwachung pausieren zu können.
- **Zielordner beim Ablegen**: `<Ablageordner-Root>\Frei\<Jahr>\<Monat>` – Jahr/Monat aus dem
  **erkannten Dokumentdatum**, nicht dem Ablage-/Importzeitpunkt.
- **Dateiname beim Ablegen (noch unverknüpft)**: `{yyyy-MM-dd}_{Titel-Slug}{ext}`, Slug ohne
  Leerzeichen (durch Bindestriche ersetzt), max. 40 Zeichen.
- **Dateiname nach erfolgreicher Verknüpfung** (wichtig, eigener Task-Wunsch in MyCoinFlow):
  Umbenennen auf `{JournalDatum:yyyy-MM-dd}_{Belegtyp}_{Gegenpartei}{ext}` – **mit Leerzeichen**
  in der Gegenpartei (keine Slug-Bindestriche hier, bessere Lesbarkeit), Kollisionsbehandlung
  mit `-2`, `-3`, … Beispiel MyCoinFlow: `2026-02-03_Rechnung_Apfelkiste Onlineshop.pdf`. In
  ShopFlow: `{Belegtyp}` ist `Kassenbeleg` oder `Rechnung`, je nachdem, welcher Verkaufsprozess
  den Journal-Eintrag erzeugt hat.
  - Datei bleibt am selben Ort (kein zweites Verschieben), nur der Name ändert sich.
- **Kategorie**: Beim Verknüpfen automatisch auf `"Rechnungen"` (bzw. `"Kassenbelege"` je nach
  Belegtyp) setzen. Kein separates Anlegen nötig, wenn Kategorie ohnehin nur ein freier
  String pro Dokument ist (kein Lookup-Table) – taucht dann automatisch im Filter auf.

---

## 5. Verknüpfen / Entkoppeln (Lifecycle)

- **Verknüpfen** (automatisch oder manuell): setzt `EntityType`/`EntityId` (bzw. FK) **und**
  löst Umbenennung + Kategorie-Zuweisung aus (Abschnitt 4) – ein Vorgang, eine Methode
  (`LinkToJournalEintrag` o. ä.), damit UI-seitig kein doppelter Code für automatischen und
  manuellen Fall nötig ist.
- **Entkoppeln ("Zurückstellen")**: `EntityType`/`EntityId` auf NULL – Datei und DB-Zeile
  bleiben erhalten, Dokument erscheint wieder als "frei" im DMS. Klar unterscheiden von
  **Löschen** (Datei + DB-Zeile werden entfernt) – beide Optionen dem User anbieten, mit
  eindeutigem Bestätigungstext, der den Unterschied klarmacht.

---

## 6. Erneutes Matching (manueller Trigger)

Motivation: Ein Beleg kann im Arbeitsordner ankommen, **bevor** der zugehörige Journal-Eintrag
gebucht ist (z. B. Rechnung kommt per Mail, aber Zahlung/Buchung erfolgt erst Tage später).
Ohne Nachtrigger bliebe das Dokument dauerhaft unverknüpft.

- **Einzeldokument**: Button "Suche erneut" auf dem ausgewählten, noch unverknüpften Dokument.
- **Alle auf einmal**: Button "Alle automatisch suchen" – iteriert über alle unverknüpften
  Dokumente und stösst für jedes das Matching erneut an.
- Beide laufen über **dieselbe Warteschlange** wie die Ersterfassung (Abschnitt 3.1), damit
  Fortschrittsanzeige und Mehrdeutigkeits-Dialog konsistent bleiben und nichts parallel läuft.
- Wichtig: **kein erneuter OCR-Lauf** – der bereits gespeicherte Text (`AttachmentText`) wird
  wiederverwendet, nur Datums-/Betrags-/Gegenpartei-Erkennung und die Matching-Query laufen
  erneut.
- Bereits verknüpfte Dokumente werden beim Bulk-Lauf automatisch übersprungen (kein Fehler,
  einfach kein Effekt).

---

## 7. UI-Komponenten (in WinUI3 neu zu bauen – kein WPF-Code übertragbar)

Diese Liste beschreibt **was** gebraucht wird, nicht wie es in WPF/MaterialDesign aussieht:

1. **Einstellungen**: Zwei Ordnerfelder (Arbeitsordner, Ablageordner) + Checkbox
   "Überwachung aktiv" + OCR-Einstellungen (Tesseract-Pfad, Sprachen). Speichern löst
   Watcher-Neustart aus.
2. **DMS-Übersicht** (Liste/Grid aller Dokumente): Volltextsuche, Kategorie-Filter,
   Fortschritts-Banner (aktuelle Datei + Phase + Warteschlangenlänge, nur sichtbar während
   Verarbeitung läuft), Spalten: Titel/Datei, Kategorie, "Verknüpft mit", Grösse, Datum, Status.
   Aktionen: Hochladen, Öffnen, Bearbeiten, Löschen, **Journal-Eintrag zuweisen**,
   **Suche erneut**, **Alle automatisch suchen**.
3. **Zuweisen-Dialog** (wiederverwendet für zwei Fälle: Mehrdeutigkeit beim Auto-Matching UND
   manuelles Zuweisen eines "freien" Dokuments): Kandidatenliste (Datum, Betrag, Gegenpartei)
   + optionales Suchfeld (Betrag-/Datumsbereich, Freitext) für den manuellen Fall.
4. **Anhänge-Dialog** (im Journal-/Verkaufsmodul): Liste der Anhänge zu einem Journal-Eintrag,
   Buttons Öffnen / **Zurückstellen** / Löschen (mit dem Unterschied klar im Bestätigungstext).
5. **Journal-#-Spalte** im Journal-Grid: analog zur `Transaktions-#`-Spalte hier – rechts nach
   dem Datum, sortierbar, erleichtert die Suche.
6. **Globaler Verarbeitungs-Indikator**: kleines rotierendes Icon/Badge an der DMS-
   Navigation, sichtbar solange der Watcher gerade etwas verarbeitet (auch wenn User gerade in
   einem anderen Modul ist).

---

## 8. Offene Punkte – vor Umsetzung in ShopFlow zu klären

- Exakte Struktur der Gegenpartei-Entität (Kunde/Lieferant getrennt oder gemeinsame Tabelle?).
- Feldnamen für Betrag/Datum im Journal-Eintrag (Mapping auf die Matching-Query in 3.2).
- Unterschiedliches Matching-Zeitfenster für Kassenbeleg (enger, da sofort bezahlt) vs.
  Rechnung (weiter, wegen Zahlungsziel) – oder reicht ein gemeinsames Fenster?
- Datenbanktechnologie in ShopFlow (SQL Server wie hier, oder etwas anderes – beeinflusst nur
  die SQL-Syntax, nicht die Architektur).
- Verhältnis zur Fibu-API: DMS bleibt rein lokal (Dokumente ↔ Journal-Einträge); die
  Fibu-Anbindung/offene Posten sind ein nachgelagerter, unabhängiger Vorgang und **nicht**
  Teil des DMS-Matchings.

---

## 9. Referenz-Implementierung (MyCoinFlow, WPF) – Dateien zum Nachschlagen

| Konzept | Datei |
|---|---|
| Textanalyse (Datum/Titel/Betrag) | `Services/DmsDocumentAnalyzer.cs` |
| Watcher/Queue/Orchestrierung | `Services/DmsWatcherService.cs` |
| OCR (PdfPig + Tesseract) | `Services/OcrService.cs` |
| Datei-/Ablage-Operationen inkl. Umbenennen bei Verknüpfung | `Services/AttachmentService.cs` |
| Generisches Attachment-Schema, Matching-Queries | `Services/DatabaseService.cs`,
  `Modules/Core/Services/DatabaseService.Core.cs` (`FindCandidateTransaktionenForMatch`,
  `SearchTransaktionenForZuordnung`) |
| Einstellungen-UI (Arbeitsordner/Ablageordner) | `Modules/Admin/Views/AdminPathsView.xaml(.cs)` |
| DMS-Übersicht | `Modules/Dms/Views/DmsView.xaml(.cs)`,
  `Modules/Dms/ViewModels/DmsViewModel.cs` |
| Zuweisen-Dialog | `Modules/Dms/Views/DmsAssignTransactionDialog.xaml(.cs)` |
| Anhänge-Dialog (Zurückstellen/Löschen) | `Modules/Core/Views/AttachmentsDialog.xaml(.cs)` |
