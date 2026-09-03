# Produktive MyCoinFlow-WinUI-Anwendung

- Dieser Ordner enthält die aktive, veröffentlichte MyCoinFlow-Anwendung und die
  Versionslinie 3.1.x.
- Verwende .NET 10 und WinUI 3. ShopFlow ist Designreferenz, nicht Fach- oder Codebasis.
- App-weite Farben, Abstände, Aktions-, Leisten- und Statusdarstellungen gehören in App.xaml,
  gezielte ResourceDictionaries oder wiederverwendbare Controls.
- Views und Code-behind koordinieren Darstellung und UI-Ereignisse. Wiederverwendbare
  Fachlogik gehört in ViewModels, Data-Repositories oder Services.
- Das Projekt referenziert MyCoinFlow/MyCoinFlow.csproj für bestehende Fach-, Modell-, Import-
  und Servicebestandteile. Diese Abhängigkeit darf nicht durch kopierte Parallelmethoden
  ersetzt werden; WPF-XAML wird dadurch nicht Teil eines normalen WinUI-Arbeitspakets.
- Eigenständige Fenster verwenden PersistentWindow und WinUiWindowStateService, sofern keine
  dokumentierte technische Ausnahme besteht.
- Umfangreiche oder fachlich gegliederte Editoren sind frei verschiebbare und skalierbare
  PersistentWindow-Fenster mit gespeichertem Zustand, benannten Datengruppen, adaptiver
  Breitenanordnung und dauerhaft sichtbaren Abschlussaktionen. ContentDialog ist auf kurze
  Bestätigungen und atomare Entscheidungen begrenzt.
- Scrollbare Formulare verwenden FormScrollViewerStyle oder reservieren gleichwertig rechts
  Platz. Abhängige Auswahlen bleiben gleichzeitig sichtbar und benennen ihren Fachkontext.
