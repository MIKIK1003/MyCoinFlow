# MyCoinFlow – WinUI 3

Dieses Projekt ist die produktive MyCoinFlow-Anwendung. Version 3.0.0.2 ist veröffentlicht;
die strukturierte Weiterentwicklung beginnt mit Version 3.1.0.0.

## Architektur

- .NET 10, WinUI 3 und Windows App SDK
- unpackaged x64-Anwendung mit eigener Login-, Aktivierungs- und Navigationsebene
- adaptive Shell mit `NavigationView`, Mica und persistenten Fensterzuständen
- produktive Bereiche für Finanzen, Immobilien, Vermögen, Haushalt, DMS, Zahlungsserien und
  Einstellungen
- gemeinsamer Mandanten- und SQL-Express-Datenbestand

Das Projekt referenziert `MyCoinFlow/MyCoinFlow.csproj`, weil dort weiterhin bewährte Fach-,
Modell-, Import-, Berichts- und Servicebestandteile liegen. Diese gemeinsame Codebasis ist
eine technische Abhängigkeit; die frühere WPF-Oberfläche ist nicht die aktive Produktlinie.

Die WinUI-Bereiche sind funktional migriert, aber gestalterisch noch nicht überall
vereinheitlicht. Die schrittweise Angleichung richtet sich nach den Verträgen unter
`DOCS/` und verwendet ShopFlow als Designreferenz.

## Start in Visual Studio

1. `MyCoinFlow.sln` öffnen.
2. `MyCoinFlow.App.WinUI` als Startprojekt auswählen.
3. Plattform `x64` wählen und starten.

Das Startprojekt für die produktive Anwendung ist `MyCoinFlow.App.WinUI`.
