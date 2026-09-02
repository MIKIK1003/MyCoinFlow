---
name: mycoinflow-version
description: MyCoinFlow-WPF-Versionsabschnitte und kurze Codex-Tasks eröffnen, fortführen oder abschließen sowie Versionsblätter, begrenzte Commits, Pushes und Archivierung führen. Verwenden bei Versionsstart, neuem Arbeitspaket, Taskwechsel oder Abschnittsabschluss in MyCoinFlow; nicht für ShopFlow oder die WinUI-Linie.
---

# MyCoinFlow-Version führen

## Quellen prüfen

Lies AGENTS.md, DOCS/VERSIONING.md, die WPF-Versionswerte in MyCoinFlow/MyCoinFlow.csproj, das
aktive Versionsblatt und den Git-Status. Geerbte Änderungen gelten als geschützter Bestand,
bis sie sicher dem Arbeitspaket zugeordnet sind.

Verwende bei Code- oder UI-Änderungen zusätzlich mycoinflow-change.

## Start

- Ein Task bearbeitet genau ein Arbeitspaket.
- Titel: <Version> · <zweistellige AP-Nummer> · <kurzer Gegenstand>.
- Lege oder aktualisiere vor der Umsetzung DOCS/VERSIONS/<Version>.md.
- Halte Ziel, Nicht-Ziele, Ausgangscommit, Verträge, Akzeptanzkriterien und Parkplatz fest.
- Fachlich unklare Anforderungen beginnen in Klärung; erfinde keinen fehlenden Ablauf.
- Erhöhe nur die WPF-Version. Die WinUI-Version bleibt ohne ausdrücklichen Auftrag unberührt.
- Neue Tasks laufen auf Wunsch des Benutzers direkt im gespeicherten Projekt
  C:\DEV\MyCoinFlow und starten vom letzten abgeschlossenen Git-Stand.

## Arbeit begrenzen

Halte den Task auf seinem Ziel. Parke unabhängige Ideen im Versionsblatt. Wenn ein zweites
eigenständiges Ergebnis entsteht oder der Umfang wesentlich wächst, schlage nach dem aktuellen
Abschluss ein neues Arbeitspaket vor.

## Abschluss

1. Prüfe Akzeptanzkriterien, relevanten Build beziehungsweise Tests und den vollständigen Diff.
2. Stage nur sicher zugeordnete Änderungen des aktuellen Arbeitspakets.
3. Aktualisiere das Versionsblatt mit Ergebnis, Prüfungen und offenen Punkten.
4. Committe und pushe nur nach ausdrücklichem Auftrag, zum Beispiel Abschnitt abschließen,
   committen und pushen. Die Erlaubnis gilt ausschließlich für das aktuelle Arbeitspaket.
5. Commitmuster: v<Version> AP<Nummer>: <Gegenstand>.
6. Archiviere den Task nach erfolgreichem Abschluss oder auf ausdrücklichen Wunsch.

Kein Commit gilt als gepusht, bevor der Remote-Push bestätigt wurde.
