---
name: mycoinflow-change
description: Änderungen in der produktiven MyCoinFlow-WinUI-3-App planen, umsetzen oder prüfen und dabei Projekt-, Architektur-, Versions- und UX-Vertrag sowie die schrittweise ShopFlow-Designangleichung bewahren. Verwenden bei Features, Fehlerbehebungen, Refactorings, UI-Arbeiten und Reviews unter MyCoinFlow.App.WinUI/; WPF-XAML nur in ausdrücklich benannten Wartungspaketen ändern.
---

# MyCoinFlow vertragsgetreu ändern

## Kontext herstellen

Lies die geltende AGENTS.md-Kette, DOCS/PROJECT_CONTRACT.md, DOCS/VERSIONING.md und das aktive
Versionsblatt. Bei UI-Arbeiten lies zusätzlich DOCS/UX_DESIGN_SYSTEM.md,
DOCS/UI_ACTION_CATALOG.md und DOCS/UI_CONTRACT_BASELINE.md. Prüfe anschließend die aktuelle
WinUI-Implementierung.

MyCoinFlow.App.WinUI/ ist die produktive Linie 3.1.x. MyCoinFlow/ bleibt als referenzierter
Fachkern und frühere WPF-Oberfläche erhalten; WPF-XAML liegt außerhalb normaler WinUI-Pakete.

## Änderung einordnen

- Gehört sie zum aktuellen Arbeitspaket? Unabhängige Einfälle kommen in den Parkplatz.
- Welcher MyCoinFlow-Seitentyp und welche ShopFlow-Masterreferenz passen fachlich?
- Welche vorhandenen WinUI-Ressourcen, Controls, ViewModels, Repositories oder Services sind
  wiederverwendbar?
- Ist sie rein visuell oder berührt sie geschützte Finanz-, Daten-, Lizenz- oder
  Updatefachlichkeit?
- Welche fremden Arbeitsbaumänderungen sind zu schützen?

Bei einem Vertragskonflikt nicht stillschweigend abweichen. Benenne Wirkung und WinUI-gerechte
Lösung; aktualisiere nach einer bestätigten Richtungsänderung die zentrale Quelle.

## Umsetzen

- Angleiche nur das beauftragte Modul oder den benannten gemeinsamen Baustein.
- Übertrage ShopFlow-Semantik, nicht Fachmodell, Datenzugriffe oder ganze Seiten.
- Lege wiederkehrende Optik in zentralen WinUI-Ressourcen oder Controls ab.
- Verwende die gemeinsame Such-/Status-/Filterreihenfolge, wo sie fachlich sinnvoll ist.
- Funktionsnamen bleiben vollständig sichtbar; Umbruch oder Scrollen geht vor Ellipse.
- Gleiche Aktionen verwenden Bezeichnung und WinUI-Iconsemantik aus dem Katalog.
- Bewahre Fensterzustand, Esc-Verhalten, Scrollbar-Reserve und sichtbare Auswahlkontexte.
- Halte Fachlogik aus rein visuellen Code-behind-Erweiterungen heraus.

## Prüfen

Führe die kleinste aussagekräftige Prüfung sowie bei angemessenem Aufwand den WinUI-Build aus.
Bei sichtbaren Änderungen prüfe normale und kleine Fensterbreite, erhöhte Skalierung,
vollständige Texte, Tastaturfokus sowie Auswahl-, Leer-, Fehler- und deaktivierte Zustände.

Prüfe abschließend den Diff auf Scope und berichte geänderte Vertragsquellen, Builds, Tests,
visuelle Prüfungen und offene manuelle Abnahmen. Commit, Push und Archivierung richten sich
nach DOCS/VERSIONING.md.
