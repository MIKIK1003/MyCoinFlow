# MyCoinFlow – Ausgangslage zum UI-Vertrag

**Prüfstand:** 2. September 2026  
**Zweck:** Übergangsstellen sichtbar halten; keine Abweichung ist eine neue Vorlage

## Tragfähige Grundlagen

- App.xaml enthält zentrale Abstände, AppBrandBrush, IconTextButton,
  IconTextButton.Danger, Navigation und DataGrid-Grundstyles.
- MainWindow besitzt eine gruppierte Navigation und sichtbare App-Version.
- BaseWindow und WindowStateService bilden eine Grundlage für Fensterzustand und Esc.
- MaterialDesign-PackIcons erlauben eine zentrale semantische Iconzuordnung.
- Git-Stand 7192aba entsprach beim Start origin/master und war sauber.

## Bekannte Konsolidierungspunkte

### Gemischte visuelle Identität

Die WPF-Ressourcen kombinieren MaterialDesign DeepPurple/Lime mit Coral. Das ShopFlow-Ziel
arbeitet mit ruhigen semantischen Flächen und Akzenten.

Ziel: semantische MyCoinFlow-Ressourcen definieren und im Pilot auf die ShopFlow-Hierarchie
abbilden. Keine weitere hart codierte Markenfarbe einführen.

### Große Navigation

MainWindow verwendet eine feste 320-Pixel-Seitenleiste, 60-Pixel-Kacheln und 12-Pixel-Radien.
Das ist brauchbarer Bestand, aber nicht die kompakte ShopFlow-Zielsprache.

Ziel: Navigation in einem eigenen Arbeitspaket angleichen. Ein Inhaltsseiten-Pilot baut sie
nicht nebenbei um.

### Kein gemeinsamer Such-/Status-/Filtermaster

28 XAML-Dateien enthalten Such- oder Filterelemente; ihre Struktur ist seitenspezifisch.

Ziel: Im ersten passenden Pilot einen WPF-Master für Suchzone, Statuslegende,
Schnellansichten und Fachfilter entwickeln.

### Lokale Styles und direkte Icons

Von 89 XAML-Dateien enthalten 36 lokale Styledefinitionen und 34 PackIcons.

Ziel: gemeinsame Ressourcen aus dem Pilot gewinnen. Lokale Varianten werden nur in bewusst
beauftragten Modulen konsolidiert.

### Vollständig sichtbare Aktionstexte

IconTextButton besitzt Mindesthöhe und Padding, garantiert aber weder responsive
Funktionsleisten noch vollständige Texte bei knapper Breite und Skalierung.

Ziel: Funktionsleistenmaster mit textabhängiger Breite, Umbruch oder horizontalem
Scroll-Fallback. Keine Ellipsen als Dauerlösung.

### Zwei UI-Technologien

Die WPF-App steht auf 2.0.2.x, MyCoinFlow.App.WinUI auf 3.0.0.2.

Ziel: Diese Angleichung gilt ausschließlich für WPF. WinUI wird separat geprüft.

