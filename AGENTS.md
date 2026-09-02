# MyCoinFlow-Arbeitsvertrag für Codex

## Geltung und Priorität

- Diese Regeln gelten für das gesamte Repository. Näher am Code liegende AGENTS.md-Dateien
  ergänzen oder konkretisieren sie.
- Verbindlich sind DOCS/PROJECT_CONTRACT.md, DOCS/ARCHITECTURE.md,
  DOCS/UX_DESIGN_SYSTEM.md und DOCS/UI_ACTION_CATALOG.md.
- DOCS/UI_CONTRACT_BASELINE.md beschreibt den Übergangsstand, nicht die Zielnorm.
- DOCS/VERSIONING.md regelt kurze Tasks, Versionsabschnitte sowie Commit-, Push- und
  Abschlussgrenzen. Der aktive Stand steht unter DOCS/VERSIONS/.
- Das frühere Working Agreement unter Dokumentation/ ist historisch und abgelöst. Frühere
  Chats gelten nicht als aktuelle Spezifikation.

## Projektgrenze

- Aktives, veröffentlichtes Produkt ist die WinUI-3-Anwendung unter MyCoinFlow.App.WinUI/.
  Die aktuelle Entwicklungslinie beginnt mit Version 3.1.0.0.
- MyCoinFlow/ enthält die frühere WPF-Oberfläche und weiterhin von WinUI referenzierte
  Fach-, Modell-, Import- und Servicebestandteile. WPF-XAML wird nur in einem ausdrücklich
  benannten Arbeitspaket geändert.
- ShopFlow ist Designreferenz, nicht Fach- oder Codebasis. Fachlogik, Datenmodell,
  Lizenzierung und technische Plattform werden nicht durch Kopieren angeglichen.

## Vor jeder Änderung

- Ordne die Änderung einer Version und genau einem Arbeitspaket zu. Tasktitel verwenden
  <Version> · <AP> · <Gegenstand>; das Versionsblatt steht unter DOCS/VERSIONS/.
- Lies die geltende AGENTS.md-Kette, Projektvertrag, Versionierungsregeln und das aktive
  Versionsblatt. Bei UI-Arbeiten lies zusätzlich UX-System, Aktionskatalog und UI-Baseline.
- Prüfe Code und XAML; Dokumentation ersetzt nicht die aktuelle Implementierung.
- Bewahre nicht zur Aufgabe gehörende Änderungen im Arbeitsbaum.

## Änderungsregeln

- Angleiche MyCoinFlow Modul für Modul. Eine lokale Aufgabe darf nicht ungefragt zum Umbau
  aller WinUI-XAML-Dateien wachsen.
- Der Wiedererkennungseffekt entsteht durch gleiche Hierarchie, Abstände, Aktionssemantik,
  Such-/Status-/Filterstruktur und Zustände, nicht durch eine kopierte Fachmaske.
- Neue gemeinsame Muster gehören in zentrale WinUI-Ressourcen oder wiederverwendbare Controls.
- Funktionsnamen bleiben vollständig sichtbar. Gleiche Aktionen verwenden innerhalb von
  MyCoinFlow dieselbe WinUI-Iconsemantik und entsprechen dem ShopFlow-Katalog.
- Bei einem Konflikt zwischen MyCoinFlow-Fachzweck und ShopFlow-Muster wird die Auswirkung vor
  der Umsetzung benannt und eine bestätigte Ausnahme zentral dokumentiert.
- Finanzdaten, Mandantentrennung, Berechtigungen, Lizenzierung und revisionsrelevante Abläufe
  bleiben von visueller Angleichung getrennt.

## Fertig bedeutet

- Das Arbeitspaket ist im Versionsblatt abgeschlossen; Nebenthemen sind geparkt.
- Der Diff enthält nur bewusst zugeordnete Änderungen.
- Relevante Builds und Prüfungen sind dokumentiert.
- Bei UI-Arbeiten sind normale und kleine Fensterbreite, erhöhte Skalierung, vollständige
  Texte, Auswahl-, Leer-, Fehler- und deaktivierte Zustände sowie Tastaturfokus geprüft.
- Geänderte Projekt-, Architektur- oder UX-Entscheidungen sind mit dem Code dokumentiert.
