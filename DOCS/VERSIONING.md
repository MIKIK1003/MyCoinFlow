# MyCoinFlow-Versionen und kurze Tasks

## Grundmodell

Der Chat ist nicht das Projektgedächtnis. Maßgeblich sind Code, AGENTS.md, DOCS-Verträge und
das Blatt der aktiven Version.

- App-Version: vierteilige Nummer in MyCoinFlow/MyCoinFlow.csproj.
- Arbeitspaket (AP): ein begrenztes Ergebnis innerhalb der Version.
- Task: genau ein Chat für genau ein Arbeitspaket.
- Versionsblatt: DOCS/VERSIONS/<Version>.md.

Die WinUI-Projektversion wird nur in ausdrücklich benannten WinUI-Arbeitspaketen geändert.

## Tasktitel und Status

Titel verwenden:

~~~text
<Version> · <zweistellige AP-Nummer> · <kurzer Gegenstand>
~~~

Zulässige Status sind Klärung, Umsetzung, Prüfung, bereit zum Abschluss, abgeschlossen und
blockiert.

## Start und Umfang

Vor der Umsetzung stehen Ziel, Nicht-Ziele, Ausgangscommit, betroffene Module, Verträge,
Akzeptanzkriterien und geplante Prüfungen im Versionsblatt. Ein unklarer Pilot bleibt in
Klärung und wird nicht mit erfundenen Fachannahmen begonnen.

Unabhängige Einfälle kommen in den Parkplatz. Nach Abschluss werden sie bewusst als neues
Arbeitspaket und neuer Task gestartet.

## Git-Abschluss

Der vollständige Abschluss wird ausgelöst mit:

~~~text
Abschnitt abschließen, committen und pushen.
~~~

Dann werden nur sicher zugeordnete Änderungen gestaged, geprüft, mit folgendem Muster
committed und auf den zugehörigen Remote-Zweig gepusht:

~~~text
v<Version> AP<Nummer>: <Gegenstand>
~~~

Ohne ausdrückliches committen und pushen erfolgt kein Remote-Push. Fehler bei Build, Test,
Commit oder Push bleiben sichtbar; der Task bleibt offen. Danach kann er archiviert werden.

## Nächsten Task starten

~~~text
Starte den nächsten MyCoinFlow-Abschnitt: <Thema>.
~~~

Codex ermittelt Version und AP-Nummer, ergänzt das Versionsblatt und erstellt den benannten
Task direkt im Projekt C:\DEV\MyCoinFlow. Für eine neue App-Version:

~~~text
Starte eine neue MyCoinFlow-Version: <Thema>.
~~~

Der neue Task startet vom letzten abgeschlossenen Git-Stand. Ein abweichender oder unsauberer
Arbeitsbaum wird ausdrücklich benannt und geschützt.

