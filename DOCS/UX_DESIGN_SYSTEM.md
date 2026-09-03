# MyCoinFlow – UX- und Designsystem

## Zielbild

MyCoinFlow übernimmt schrittweise die ShopFlow-Designsprache: ruhig, präzise,
informationsreich und klar hierarchisiert. Die WinUI-3-Anwendung bleibt ein eigenes Produkt.
Wiedererkennung entsteht durch dieselben Grundmuster und Aktionssemantiken, nicht durch das
Kopieren fachfremder ShopFlow-Inhalte.

Die visuelle Referenz steht unter DOCS/REFERENCES/SHOPFLOW_UI_MASTER_2026-09-02.md.

## ShopFlow-Master nach Seitentyp

- Dichte Transaktions-, DMS- und Finanzarbeit orientiert sich an ShopFlow Debitoren.
- Bestände, Konten, Adressen, Abos und Vermögen orientieren sich an der
  Listen-/Detailgrammatik von ShopFlow Vermietung.
- Import-, Zuordnungs- und Bearbeitungsfortschritte orientieren sich bei echten Statusabläufen
  an ShopFlow Reparatur.
- Einstellungen und abhängige Immobilien-Stammdaten behalten ihre fachlich notwendige
  Struktur, verwenden aber dieselben Grundressourcen und Aktionsregeln.

Die Referenz wird pro Arbeitspaket gewählt. Ein Modul wird nicht allein aufgrund eines ähnlich
aussehenden Screens einem Seitentyp zugeordnet.

## Gemeinsame Such-/Status-/Filterzeile

In geeigneten Arbeitsbereichen gilt dieselbe Reihenfolge:

1. Suchzone links, flexibel wachsend,
2. kompakte Statuslegende mit Symbol, Farbe und lesbarer Bezeichnung,
3. segmentierte Schnellansichten,
4. Fachfilter mit sichtbarer Bezeichnung Filter.

Eine fachlich nicht sinnvolle Zone darf entfallen. Die übrigen Zonen werden nicht lokal neu
gestaltet oder umsortiert. Mehrere notwendige Suchfelder bleiben gemeinsam in der Suchzone.
Höhe, Rahmen, Abstände, Auswahlzustände und Tastaturfokus werden zentral als WinUI-Ressourcen
beziehungsweise gemeinsames Control umgesetzt.

## Gemeinsame Funktionsleiste

- Sie steht unter dem Seitenkopf und vor Suche beziehungsweise Inhalt.
- Aktionen sind fachlich gruppiert und durch ruhige Trenner strukturiert.
- Icon und Text bleiben als Einheit sichtbar; Beschriftungen werden nicht abgeschnitten.
- Bei knapper Breite entstehen zuerst passende Spalten oder zusätzliche Zeilen; danach ist
  horizontales Scrollen besser als Textverlust.
- Gleiche Wirkung verwendet Bezeichnung und WinUI-Iconsemantik aus DOCS/UI_ACTION_CATALOG.md.
- Höchstens eine Einstiegs- oder nächste Aktion ist primär.
- Gefährliche Aktionen verwenden einen eigenen Stil und Abstand.
- Nicht anwendbare Funktionen werden kontextgerecht deaktiviert oder ausgeblendet, ohne große
  tote Leistenbereiche.

## Layout und Zustände

- Abstände folgen einem 4-/8-Pixel-Raster; übliche Inhaltsränder liegen bei 16 bis 24 Pixeln.
- Kompakte Desktopaktionen sind üblicherweise 32 bis 40 Pixel hoch.
- Standardradius: 4 Pixel für kompakte Elemente, 6 Pixel für größere Flächen.
- Tabellen und Listen zeigen stabile Überschriften, Auswahl und Leerzustand.
- Detailbereiche benennen eindeutig, auf welchen Datensatz Aktionen wirken.
- Status ist nie nur über Farbe verständlich.
- Icons ergänzen wichtige Begriffe; sie ersetzen sie nur bei allgemein bekannten Aktionen mit
  Tooltip und zugänglichem Namen.

## WinUI-spezifische Regeln

- ShopFlow-Semantik wird mit WinUI-Controls, ThemeResources und gemeinsamen Styles umgesetzt.
  Ganze ShopFlow-Seiten, fachliche Services und lokale Sonderlösungen werden nicht kopiert.
- Standardaktionen verwenden den zentralen `ActionIconStyle` und semantisch benannte
  native WinUI-FontIcon-Glyphen mit Grundgröße 16. Die Fontfamilie folgt wie beim
  AppBarButton dem WinUI-Theme; so bleiben Kontur, Feinheit und Skalierung in Funktionsleisten
  einheitlich. SymbolIcon wird dort wegen seiner sichtbar abweichenden, gröberen Geometrie
  nicht verwendet; rohe Glyphcodes werden nicht je Seite neu gewählt.
- App-weite Farben erhalten semantische Ressourcennamen. Die vorhandenen Teal-, Positiv- und
  Gefahrenressourcen sind Ausgangsbestand und werden nicht lokal dupliziert.
- Neue Seiten hardcodieren keine weitere Markenfarbe. Eine produktspezifische Abweichung
  benötigt eine bewusste zentrale Entscheidung.
- Die vorhandene NavigationView bleibt für schmale Breiten adaptiv. Inhaltsseiten ergänzen
  eigene VisualStates oder kontrolliertes horizontales Scrollen, wenn ihre Struktur sonst
  Texte oder Aktionen abschneidet.
- Eigenständige Fenster bewahren Position, normale Größe und Maximierungszustand monitor-
  sicher. Minimierte Fenster werden nicht minimiert wiederhergestellt.
- Eigenständige Arbeitsfenster lassen sich mit Esc schließen beziehungsweise abbrechen; das
  Hauptfenster bleibt ausgenommen.
- Umfangreiche oder fachlich gegliederte Datenerfassung wird als frei verschiebbares und
  skalierbares eigenständiges Fenster umgesetzt. `ContentDialog` bleibt kurzen Bestätigungen
  und atomaren Entscheidungen vorbehalten; es ist kein Container für mehrteilige
  Stammdatenformulare.
- Editorfenster verwenden `PersistentWindow`, bewahren Position, normale Größe und
  Maximierungszustand und halten Kopfkontext sowie Speichern/Abbrechen unabhängig vom
  scrollbaren Formularinhalt sichtbar.
- Zusammengehörige Daten stehen in benannten Karten und bei ausreichender Breite
  nebeneinander. Bei schmaler Breite werden die Gruppen vollständig gestapelt oder über
  eindeutig bezeichnete Register gegliedert; eine unstrukturierte lineare Feldkette ist
  kein Zielmuster.
- Scrollbare Formulare verwenden FormScrollViewerStyle oder reservieren gleichwertig rechts
  Platz für die Scrollbar.
- Abhängige Auswahlen bleiben räumlich zusammen; die wirksame Auswahlkette ist sichtbar und
  technische IDs sind nur ein erkennbarer Fallback.

## Migrationsregel

Unveränderte Seiten dürfen vorübergehend im bisherigen Stil bleiben. Eine bearbeitete
Pilotseite wird als zusammenhängender Bereich auf das Zielbild gebracht und liefert gemeinsame
Bausteine für die nächste Seite. Eine Fachaufgabe darf bekannte globale Abweichungen nicht
nebenbei vollständig sanieren, sie aber auch nicht vergrößern.
