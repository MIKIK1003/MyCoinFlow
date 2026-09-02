# MyCoinFlow – Aktions- und Iconkatalog

**Stand:** 2. September 2026  
**Status:** verbindlich für neue und angeglichene WinUI-Oberflächen

Gleiche Aktionen verwenden dieselbe Grundbezeichnung und dieselbe WinUI-Iconsemantik.
Sichtbare Standardaktionen verwenden ein natives WinUI-FontIcon mit dem zentralen
`ActionIconStyle` und einer semantisch benannten Glyph-Ressource. Die Grundgröße 16 und
Layout-Rundung ergeben die feine, skalierbare Darstellung des WinUI-Masters; die Fontfamilie
bleibt wie bei AppBarButton dem WinUI-Theme überlassen.
SymbolIcon wird in Funktionsleisten nicht verwendet, weil dessen abweichende Symbolgeometrie
bei dieser Größe sichtbar gröber wirkt. Spezielle IconSource-Varianten bleiben möglich, werden
aber ebenfalls zentral benannt; rohe Glyphcodes werden nicht lokal wiederholt.

| Semantik | Bevorzugte Beschriftung | WinUI-Icon | Stil |
|---|---|---|---|
| neue Entität anlegen | Neu, Neuer …, Neue …, Hinzufügen | ActionAddIconGlyph | primär als Seiteneinstieg |
| bestehendes Objekt öffnen | Öffnen, … öffnen | ActionOpenIconGlyph | normal |
| Daten bearbeiten | Bearbeiten | ActionEditIconGlyph | normal |
| speichern | Speichern | zentrale Save-Glyph | primär im Dialog |
| suchen | Suchen | ActionSearchIconGlyph | normal |
| filtern | Filter | ActionFilterIconGlyph | normal |
| aktualisieren oder nachführen | Aktualisieren, Nachführen | ActionRefreshIconGlyph | normal |
| PDF ausgeben | PDF, Als PDF speichern | gemeinsame PDF-IconSource | normal |
| E-Mail senden | E-Mail, Per E-Mail senden | zentrale Mail-Glyph | normal |
| Dokument oder Anhang zuordnen | Dokument, Anhang | ActionAttachIconGlyph | normal |
| nächsten Prozessschritt ausführen | Nächster Schritt | zentrale Forward-Glyph | primär |
| nach oben verschieben | Nach oben | gemeinsame Nach-oben-IconSource | normal |
| nach unten verschieben | Nach unten | gemeinsame Nach-unten-IconSource | normal |
| löschen oder entfernen | Löschen, Entfernen | ActionDeleteIconGlyph | Gefahr |
| abbrechen oder schließen | Abbrechen, Schließen | zentrale Cancel-Glyph | sekundär |

## Regeln

- Die Beschriftung benennt die Wirkung, nicht die technische Methode.
- Fachgegenstände dürfen ergänzt werden, etwa Buchung löschen.
- Abkürzungen kaschieren keine zu schmale Aktionsfläche.
- Tooltip und Tastenkürzel ersetzen keine notwendige sichtbare Beschriftung.
- Ein gleichbedeutendes Symbol wird nicht lokal aus Geschmacksgründen ersetzt.
- Aktionsicons bleiben FontIcon-Glyphen in Regular-Anmutung; Filled-Varianten sind
  Statuskennzeichnungen vorbehalten.
- Eine wiederkehrende neue Aktion wird spätestens bei der zweiten Verwendung zentral ergänzt.
- Cross-App-Konsistenz bedeutet gleiche Semantik; MyCoinFlow verwendet dafür seine zentralen
  WinUI-Ressourcen und kopiert keine lokalen ShopFlow-Glyphcodes.
