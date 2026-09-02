# MyCoinFlow – Architekturgrenzen

## Aktive Anwendung

Die produktive Versionslinie 2.0.2.x ist MyCoinFlow/MyCoinFlow.csproj auf .NET 9, WPF und
MaterialDesignThemes. MyCoinFlow.App.WinUI ist getrennt versioniert und nicht Teil eines
normalen WPF-Arbeitspakets.

## Verantwortlichkeiten

- Modules/<Fachbereich>/Views enthält WPF-Views und Dialoge.
- Modules/<Fachbereich>/ViewModels enthält darstellungsbezogenen Zustand und Befehle.
- Modules/<Fachbereich>/Services sowie Services enthält Fach-, Integrations- und
  Datenzugriffslogik.
- Shared enthält Basisklassen, Hilfen und wiederverwendbare UI-Bausteine.
- App.xaml und gezielt ausgelagerte ResourceDictionaries enthalten zentrale Designressourcen.
- MainWindow stellt Hauptnavigation und aktiven Inhaltsbereich bereit.

Die partiellen DatabaseService-Dateien sind ein bestehender Übergang. Neue Fachlogik wird dem
passenden Modul oder Service zugeordnet und nicht allein aus Bequemlichkeit in Code-behind
oder eine allgemeine Sammeldatei gelegt.

## Geschützte Invarianten

- Mandanten- und Benutzerkontext bleiben bei Datenzugriffen erhalten.
- Lizenzierte Module werden nicht durch UI-Umbauten freigeschaltet oder umgangen.
- Buchungen, DMS-Zuordnungen, Import-, Update- und revisionsrelevante Abläufe behalten
  Zustände und Transaktionsgrenzen.
- Ein Design-Arbeitspaket verändert ohne ausdrücklichen Auftrag weder Datenbankschema noch
  Berechnungs- oder Buchungslogik.

## Gemeinsame UI-Bausteine

Wiederkehrende ShopFlow-Muster werden als WPF-Ressource oder gemeinsames Control eingeführt.
Ein Pilot darf eine lokale Vorstufe enthalten, wenn Wiederverwendung noch nicht belegt ist;
spätestens bei der zweiten Verwendung wird sie zentralisiert.

