# MyCoinFlow – Engineering & Coding Standard

> **Ziel dieses Dokuments**
> Dieses Dokument ist die verbindliche Referenz („Single Source of Truth“) für Architektur‑, Coding‑ und UI‑Entscheide im Projekt **MyCoinFlow**.
> Es dient dazu, **Inkonsistenzen zwischen Tasks zu verhindern** und sicherzustellen, dass neue Features den bestehenden, bewährten Mustern folgen.

---

## 1. Grundprinzipien

### 1.1 Stabilität vor Cleverness

* Lieber **klar, explizit, defensiv** als elegant oder abstrakt.
* Keine impliziten Annahmen (z. B. Vorzeichen, Default‑Werte).
* Jede wichtige Regel ist **einmal zentral** definiert.

### 1.2 Single Source of Truth

* **DatabaseService** ist die **einzige** Stelle für SQL und Datenzugriff.
* **ViewModel** entscheidet über Darstellung/Logik.
* **UI (XAML)** enthält **keine Fachlogik**.

### 1.3 Einheitlicher Stil über alle Tasks

* Ein neuer Task darf **keine bestehenden Muster brechen**.
* Wenn ein Muster geändert wird, **dokumentieren** (siehe ADRs).

---

## 2. Architektur

### 2.1 MVVM (verbindlich)

* **View (XAML)**

  * Bindings, Styles, Layout
  * Keine Logik, keine SQL, keine Berechnungen

* **ViewModel**

  * Commands (`RelayCommand`)
  * Lade‑/Speicherlogik (`LoadX`, `RefreshX`)
  * Vorzeichen‑, Status‑, Filter‑Logik

* **DatabaseService**

  * Alle SQL‑Statements
  * Schema‑Initialisierung (`Ensure…Schema()`)
  * Keine UI‑Abhängigkeiten

---

## 3. Datenzugriff & Datenbank

### 3.1 DatabaseService – Regeln

* **Alle** DB‑Zugriffe laufen über `DatabaseService`

* Methoden sind:

  * klar benannt (`StweSetInsert`, `StweSetsGetByLiegenschaft`)
  * defensiv (null‑Checks, Try/Catch wo sinnvoll)

* Schema‑Änderungen:

  * immer **idempotent** (`IF COL_LENGTH(...) IS NULL`)

### 3.2 Keine doppelte Logik

* Wenn eine Regel im DB‑Layer existiert, darf sie im UI **nicht erneut erfunden** werden.
* Anzeige‑Spezifika (z. B. Vorzeichen) gehören **ins ViewModel**.

---

## 4. Vorzeichen‑ & Saldo‑Logik (sehr wichtig)

### 4.1 Begriffe

* **Belastung** = Ausgabe / Rechnung → **positiver Betrag**
* **Gutschrift** = Einzahlung / Rückvergütung → **negativer Betrag**

### 4.2 Sets (STWE)

* Jedes Set hat ein explizites Flag:

  * `IsCredit = true` → Gutschrift
  * `IsCredit = false` → Belastung

* **Manuelles Umschalten** ist erlaubt

  * beim Umschalten werden bestehende `SetLines` **gespiegelt** (Betrag × −1)

### 4.3 Anzeige‑Normalisierung (verbindlich)

> **Regel:** Vorzeichen werden **genau einmal** im **ViewModel** normalisiert.

```csharp
var abs = Math.Abs(set.Betrag);
var signed = set.IsCredit ? -abs : abs;
set.Betrag = signed;
set.Rest = signed - set.Verteilt;
```

* Egal ob die DB signed oder unsigned liefert
* Keine Vorzeichenlogik in XAML

---

## 5. UI‑Standards

### 5.1 Layout

* **Linke Sidebar** für Aktionen & Filter
* **Rechter Bereich** für Grid / Inhalt
* Sidebar darf nach unten wachsen

### 5.2 Buttons

* Einheitlich:

  * `MaterialDesignRaisedButton`
  * `PackIcon` links, Text rechts

* Keine Mischformen (Outlined / Flat nur mit Begründung)

### 5.3 Icons (MaterialDesign)

* Belastung → ↑ (`ArrowUp…`)
* Gutschrift → ↓ (`ArrowDown…`)
* Edit → Stift
* Delete → Papierkorb

Icons sind **rein visuell**, nie Logik‑Trigger.

---

## 6. Filter & Zeiträume

### 6.1 Default‑Zeitraum

* Wenn vorhanden, wird **immer** der **aktive Budgetzeitraum** verwendet:

  * Tabelle `Budgetzeitraum`
  * `IstAktiv = 1`

Gilt für:

* Berichte
* Transaktion‑Grid
* Set‑Grid

### 6.2 Reset‑Verhalten

* „Clear Filter“ setzt **nicht auf NULL**, sondern zurück auf:

  * aktiven Budgetzeitraum

---

## 7. Reports & Druck

### 7.1 FlowDocument

* A4, Portrait
* `ColumnWidth = Infinity`
* Tabellen mit fixer Spaltenlogik

### 7.2 Summen

* Summenzeilen **immer explizit**
* Bei Eigentümer‑Summen:

  * Summe < 0 → **Eigentümerguthaben**
  * Summe > 0 → **Fehlbetrag**
  * Summe = 0 → **Ist ausgeglichen**

---

## 8. Arbeiten mit Tasks

### 8.1 Neue Tasks

Bei Start eines neuen Tasks:

* dieses Dokument gilt automatisch
* Abweichungen müssen **explizit** begründet werden

### 8.2 Änderungen an Standards

* nur bewusst
* idealerweise mit Mini‑ADR:

  * Kontext
  * Entscheidung
  * Konsequenz

---

## 9. Arbeitsweise mit KI (verbindlich)

* **Keine Alternativen** („entweder/oder“)
* **Ganze Dateien** statt Schnipsel
* Änderungen **minimal & gezielt**
* Bestehende Logik nicht überschreiben, ohne sie zu verstehen

---

## 10. Schluss

Dieses Dokument schützt:

* Konsistenz
* Wartbarkeit
* Fachlogik

> **Wenn etwas unsicher ist:**
> lieber hier nachschärfen als im Code improvisieren.
