# MyCoinFlow – Ausgangslage zum UI-Vertrag

**Prüfstand:** 2. September 2026  
**Zweck:** Übergangsstellen sichtbar halten; keine Abweichung ist eine neue Vorlage

## Tragfähige Grundlagen

- App.xaml enthält semantische Akzent-, Positiv- und Gefahrenfarben sowie PageTitleStyle,
  CardBorderStyle, FunctionBarStyle und FormScrollViewerStyle.
- MainWindow besitzt eine adaptive NavigationView, Mica und sichtbaren Mandanten- und
  Benutzerkontext.
- PersistentWindow und WinUiWindowStateService bilden die gemeinsame Grundlage für
  monitor-sichere Fensterzustände.
- Die aktive App umfasst 67 WinUI-XAML-Dateien, darunter 14 Seiten, 17 Fenster und 25 Dialoge.
- Git-Stand 501f3b1 entsprach beim Start der Korrektur origin/master und war sauber.

## Bekannte Konsolidierungspunkte

### Produktstatus und Preview-Reste

Die WinUI-App ist produktiv veröffentlicht. Beim Start von 3.1.0.0 bezeichneten README,
Fenstertitel, Login und eine Transaktions-InfoBar sie noch als Preview oder Musterversion.

Ziel: Diese überholten Kennzeichnungen im Versionsstart entfernen und künftige
Produktinformation nicht mehr an den früheren Migrationsstatus koppeln.

### Unterschiedliche Funktionsleisten

31 WinUI-XAML-Dateien enthalten CommandBars. Daneben bestehen FunctionBarStyle, einfache
Buttonzeilen und seitenspezifische Gruppierungen.

Ziel: Im Pilot eine gemeinsame Hierarchie und Gruppierung festlegen, ohne alle bestehenden
Seiten nebenbei umzubauen.

### Kein gemeinsamer Such-/Status-/Filtermaster

Such- und Filterelemente sind bereits in mehreren produktiven Seiten vorhanden, ihre
Reihenfolge, Statusdarstellung und Schnellansichten bleiben jedoch seitenspezifisch.

Ziel: Im ersten passenden Pilot einen WinUI-Master für Suchzone, Statuslegende,
Schnellansichten und Fachfilter entwickeln.

### Direkte Icons und lokale Varianten

Viele Views verwenden rohe FontIcon-Glyphcodes oder lokal gewählte Symbolnamen. Dadurch kann
dieselbe Aktion zwischen Seiten unterschiedlich aussehen.

Ziel: katalogisierte Standardaktionen mit dem zentralen FontIcon-Stil und semantisch benannten
Glyph-Ressourcen umsetzen. Spezielle IconSource-Varianten werden ebenfalls zentral benannt.
Lokale Varianten werden nur im beauftragten Modul konsolidiert.

### Vollständig sichtbare Aktionstexte

FunctionButtonLabelStyle verwendet derzeit CharacterEllipsis und eine Zeile. Das widerspricht
dem Ziel vollständig sichtbarer Funktionsnamen bei knapper Breite und Skalierung.

Ziel: Funktionsleistenmaster mit textabhängiger Breite, Umbruch oder horizontalem
Scroll-Fallback. Keine Ellipsen als Dauerlösung.

### Seitenspezifische Responsivität

Die NavigationView besitzt sinnvolle Breakpoints; in den Inhaltsseiten sind jedoch keine
VisualState-Definitionen vorhanden. Breite Kennzahlen-, Filter- und Listengrids können deshalb
bei kleiner Fensterbreite Text oder Aktionen verdrängen.

Ziel: Der Pilot definiert ein belastbares Verhalten für normale und kleine Breiten sowie
erhöhte Skalierung.

### Frühere WPF-Anwendung als Abhängigkeit

MyCoinFlow.App.WinUI referenziert MyCoinFlow/MyCoinFlow.csproj für vorhandenen Fachcode.

Ziel: Die Designangleichung gilt für WinUI. Gemeinsam genutzte Fachlogik bleibt erhalten;
WPF-XAML wird nicht automatisch mitgeändert.
