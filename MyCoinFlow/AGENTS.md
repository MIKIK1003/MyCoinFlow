# Produktive MyCoinFlow-WPF-Anwendung

- Dieser Ordner enthält die aktive Anwendung der Versionslinie 2.0.2.x.
- Verwende WPF und MaterialDesignThemes; übertrage keine WinUI-Controls, Segoe-Glyphcodes oder
  APIs wörtlich aus ShopFlow.
- App-weite Farben, Abstände, Button-, Leisten- und Statusdarstellungen gehören in zentrale
  ResourceDictionaries beziehungsweise zunächst in App.xaml, nicht als neue lokale Kopie in
  eine View.
- Views und Code-behind koordinieren Darstellung und UI-Ereignisse. Wiederverwendbare
  Fachlogik gehört in ViewModels oder Services.
- Eigenständige Fenster verwenden die gemeinsame BaseWindow-Infrastruktur, sofern keine
  dokumentierte technische Ausnahme besteht.
- Scrollbare Formulare reservieren rechts Platz; Scrollbars überlagern keine Eingaben oder
  Aktionen.
- Zusammenhängende abhängige Auswahlen bleiben gleichzeitig sichtbar und benennen ihren
  aktuellen Fachkontext.

