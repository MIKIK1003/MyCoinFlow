# MyCoinFlow – Architekturgrenzen

## Aktive Anwendung

Die produktive Versionslinie 3.1.x ist
MyCoinFlow.App.WinUI/MyCoinFlow.App.WinUI.csproj auf .NET 10, WinUI 3 und Windows App SDK.
Die Anwendung läuft unpackaged auf x64.

## Verantwortlichkeiten

- MyCoinFlow.App.WinUI/Views enthält WinUI-Seiten, Fenster, Dialoge und Einstellungscontrols.
- MyCoinFlow.App.WinUI/ViewModels enthält darstellungsbezogenen Zustand und Befehle.
- MyCoinFlow.App.WinUI/Data kapselt WinUI-spezifische Repositories und Datenzugriffe.
- MyCoinFlow.App.WinUI/Services enthält WinUI-spezifische Abläufe und Infrastruktur.
- MyCoinFlow.App.WinUI/Models enthält WinUI-Anzeige- und Übergabemodelle.
- App.xaml und gezielt ausgelagerte ResourceDictionaries enthalten zentrale Designressourcen.
- MainWindow stellt Login, NavigationView, Mandanten-/Benutzerkontext und aktiven Frame bereit.
- PersistentWindow und WinUiWindowStateService bilden die gemeinsame Fensterinfrastruktur.

MyCoinFlow.App.WinUI referenziert MyCoinFlow/MyCoinFlow.csproj. Bewährte Fach-, Modell-,
Import-, Berichts- und Servicebestandteile aus diesem Projekt bleiben die gemeinsame
Implementierung. Die frühere WPF-Oberfläche ist keine Design- oder Navigationsvorlage für
neue WinUI-Arbeit.

Code-behind darf WinUI-Ereignisse, Dialoge und Darstellungszustände koordinieren. Neue
wiederverwendbare Fach- oder Datenzugriffslogik wird dem passenden ViewModel, Repository oder
Service zugeordnet und nicht allein aus Bequemlichkeit in eine Seite gelegt.

## Geschützte Invarianten

- Mandanten- und Benutzerkontext bleiben bei Datenzugriffen erhalten.
- Lizenzierte Module werden nicht durch UI-Umbauten freigeschaltet oder umgangen.
- Buchungen, DMS-Zuordnungen, Import-, Update- und revisionsrelevante Abläufe behalten
  Zustände und Transaktionsgrenzen.
- Ein Design-Arbeitspaket verändert ohne ausdrücklichen Auftrag weder Datenbankschema noch
  Berechnungs- oder Buchungslogik.

## Gemeinsame UI-Bausteine

Wiederkehrende ShopFlow-Muster werden als WinUI-Ressource oder gemeinsames Control eingeführt.
Ein Pilot darf eine lokale Vorstufe enthalten, wenn Wiederverwendung noch nicht belegt ist;
spätestens bei der zweiten Verwendung wird sie zentralisiert.
