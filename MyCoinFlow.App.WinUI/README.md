# MyCoinFlow 3 – WinUI Preview

Dieses Projekt wird parallel zur bestehenden WPF-Anwendung entwickelt. Es verwendet denselben
Mandanten und dieselbe SQL-Express-Datenbank. Die produktive WPF-Anwendung bleibt als Projekt
`MyCoinFlow` unverändert startbar.

## Vollständiger Vertical Slice „Transaktionen“

- moderner Einstieg mit Mandantenauswahl, Benutzerlogin und Aktivierungsprüfung
- adaptive WinUI-3-Shell mit `NavigationView`, dauerhaft sichtbarer CommandBar und Mica
- Transaktionsliste mit Suche, Zeitraum, Konto-, Adress- und Betragsfiltern
- Neuanlage, Bearbeitung und geschütztes Löschen der bestehenden Buchungswege
- Dokumente verknüpfen, direkt öffnen und verwalten; bei mehreren Dokumenten mit Auswahl
- Dublettensuche mit dem bestehenden Schutz vor abhängigen Daten
- Bankimport als eigenes WinUI-Arbeitsfenster mit dem bestehenden CAMT.053-Parser
- unveränderter Bankimport-Ablauf: Datei prüfen, leeren, Staging speichern/laden, Zuordnung,
  Einzelbuchung oder Bulk-Verbuchung
- dieselben bestehenden Regeln für Adresserkennung, interne Umbuchung, Sonderregeln,
  Geldinstitut, Importhash und Duplikatprüfung beim Verbuchen
- Kreditkartenimport als eigenes WinUI-Arbeitsfenster mit dem bestehenden Excel-Mapping,
  Batch-Staging und Duplikatprüfung bereits beim Einlesen
- gemeinsame WinUI-Zuordnungsmaske für Bank und Kreditkarte mit Adressen, Budgeteinnahmen,
  Standardkonto, Konto-Schnellwahl, Contains-Suche, Alias- und Sonderregeln
- vollständiges bestehendes Berichtswesen mit Budgetzeitraum, Nummernkreisen, Kontenauswahl,
  Berichtsarten, Gruppierung, Soll/Ist, Jahreshochrechnung, Spotlight, Budgetanpassung und
  Drucken beziehungsweise PDF

Die Import- und Berichtsalgorithmen werden direkt aus dem bestehenden Projekt `MyCoinFlow`
verwendet. Das WinUI-Projekt ersetzt nur Darstellung und Fensterführung.

Noch nicht migrierte Navigationsziele sind sichtbar, aber deaktiviert.

## Start in Visual Studio

1. `MyCoinFlow.sln` öffnen.
2. `MyCoinFlow.App.WinUI` als Startprojekt auswählen.
3. Plattform `x64` wählen und starten.

Die bestehende WPF-Anwendung bleibt das Startprojekt `MyCoinFlow`, solange Version 3 noch nicht
releasefähig ist.
