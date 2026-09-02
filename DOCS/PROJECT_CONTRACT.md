# MyCoinFlow – verbindlicher Projektvertrag

**Stand:** 2. September 2026  
**Status:** verbindlich  
**Aktive Produktlinie:** WPF, Version 2.0.2.x  
**Designreferenz:** ShopFlow

## Zweck

Dieser Vertrag bewahrt die vereinbarte Richtung über kurze Codex-Tasks hinweg. MyCoinFlow wird
schrittweise an die ruhige, präzise und informationsreiche ShopFlow-Designsprache angeglichen,
ohne seinen anderen Fachzweck oder seine WPF-Technik zu verleugnen.

Er ersetzt das frühere MyCoinFlow Working Agreement v1.1 als aktive Vereinbarung. Nützliche
technische Invarianten daraus wurden übernommen; die alte Datei bleibt nur als Historie.

## Verbindliche Quellen

1. AGENTS.md: Geltung, Scope und Arbeitsregeln
2. DOCS/PROJECT_CONTRACT.md: Projektgrenze und Entscheidungsprozess
3. DOCS/ARCHITECTURE.md: technische Grenzen
4. DOCS/UX_DESIGN_SYSTEM.md: Zielbild und Migrationsregeln
5. DOCS/UI_ACTION_CATALOG.md: gleiche Aktion, Bezeichnung und Iconsemantik
6. DOCS/VERSIONING.md und DOCS/VERSIONS/: kurze Tasks und aktueller Stand
7. aktuelle MyCoinFlow-Implementierung und ausdrücklich benannte Fachanforderungen

DOCS/UI_CONTRACT_BASELINE.md hält bestehende Abweichungen sichtbar. Eine bestehende Abweichung
ist keine erlaubte Vorlage.

## Verhältnis zu ShopFlow

ShopFlow ist die Designreferenz:

- Debitoren für dichte Vorgangs-, Dokument- und Finanzarbeit,
- Vermietung für Bestands-/Vertrags- und Listen-/Detailarbeit,
- Reparatur für Status- und Workflowdarstellungen.

Übernommen werden visuelle Grammatik und Bediensemantik: Seitenhierarchie,
Such-/Status-/Filterzeile, Funktionsleisten, Abstände, Statusbilder, vollständige
Beschriftungen, Aktionsorte und konsistente Icons.

Nicht automatisch übernommen werden Fachmodell, konkrete Texte, Datenbankzugriffe,
ShopFlow-Services, WinUI-Controls oder der Aufbau eines fachlich unpassenden Moduls. Die
technische Umsetzung bleibt WPF-gerecht.

## Unveränderliche Grundsätze

- MyCoinFlow wirkt ruhig, präzise, effizient und vertrauenswürdig.
- Gleichartige Aufgaben sehen gleich aus und verhalten sich gleich.
- Suche, Statuslegende, Schnellansicht und Fachfilter bilden in geeigneten Seiten dieselbe
  wiedererkennbare Zeile.
- Funktionsleisten zeigen vollständige Texte; Ellipsen oder Tooltip-only sind keine Lösung.
- Gleiche Aktionen verwenden dieselbe Bezeichnung und dieselbe Iconsemantik.
- Eine Seite erhält höchstens eine hervorgehobene Einstiegs- oder nächste Aktion.
- Gefährliche Aktionen stehen getrennt von normalen Aktionen.
- Gemeinsame Regeln werden zentral umgesetzt.
- Finanzfachlichkeit, Datenintegrität, Mandantenbezug, Lizenzprüfung und Updatefähigkeit
  werden durch Designarbeit nicht verändert.

## Schrittweise Angleichung

Jedes Arbeitspaket wählt einen sinnvollen Pilotbereich oder einen klar abgegrenzten gemeinsamen
Baustein. Vor dem Umbau werden Seitentyp, ShopFlow-Referenz, Akzeptanzkriterien und bewusste
Ausnahmen festgelegt. Ein fertiger Pilot liefert wiederverwendbare Ressourcen für spätere
Module, ohne unveränderte Seiten nebenbei umzubauen.

Innerhalb einer angeglichenen Seite bleibt das Zielbild konsistent. Neue Elemente dürfen dort
nicht wieder auf den alten lokalen Stil zurückfallen.

## Umgang mit neuen Entscheidungen

1. Vertrag, Versionsblatt und aktuelle Implementierung prüfen.
2. Passende ShopFlow-Masterreferenz und WPF-Übersetzung bestimmen.
3. Konflikte oder fachlich notwendige Ausnahmen vor der Umsetzung benennen.
4. Nach bestätigter Richtungsänderung die zentrale Quelle im selben Änderungssatz anpassen.
5. Ersetzte Regeln als historisch kennzeichnen; keine zwei aktiven Standards führen.

