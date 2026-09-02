# MyCoinFlow – Aktions- und Iconkatalog

**Stand:** 2. September 2026  
**Status:** verbindlich für neue und angeglichene WPF-Oberflächen

Gleiche Aktionen verwenden dieselbe Grundbezeichnung und dasselbe MaterialDesign-PackIcon.
Die Bedeutung entspricht ShopFlow; die konkrete Icontechnik bleibt WPF.

| Semantik | Bevorzugte Beschriftung | PackIcon Kind | Stil |
|---|---|---|---|
| neue Entität anlegen | Neu, Neuer …, Neue …, Hinzufügen | Plus | primär als Seiteneinstieg |
| bestehendes Objekt öffnen | Öffnen, … öffnen | FolderOpen | normal |
| Daten bearbeiten | Bearbeiten | Pencil | normal |
| speichern | Speichern | ContentSave | primär im Dialog |
| suchen | Suchen | Magnify | normal |
| filtern | Filter | FilterOutline | normal |
| aktualisieren oder nachführen | Aktualisieren, Nachführen | Refresh | normal |
| PDF ausgeben | PDF, Als PDF speichern | FilePdfBox | normal |
| E-Mail senden | E-Mail, Per E-Mail senden | EmailOutline | normal |
| Dokument oder Anhang zuordnen | Dokument, Anhang | Paperclip | normal |
| nächsten Prozessschritt ausführen | Nächster Schritt | ArrowRight | primär |
| nach oben verschieben | Nach oben | ArrowUp | normal |
| nach unten verschieben | Nach unten | ArrowDown | normal |
| löschen oder entfernen | Löschen, Entfernen | DeleteOutline | Gefahr |
| abbrechen oder schließen | Abbrechen, Schließen | Close | sekundär |

## Regeln

- Die Beschriftung benennt die Wirkung, nicht die technische Methode.
- Fachgegenstände dürfen ergänzt werden, etwa Buchung löschen.
- Abkürzungen kaschieren keine zu schmale Aktionsfläche.
- Tooltip und Tastenkürzel ersetzen keine notwendige sichtbare Beschriftung.
- Ein gleichbedeutendes PackIcon wird nicht lokal aus Geschmacksgründen ersetzt.
- Eine wiederkehrende neue Aktion wird spätestens bei der zweiten Verwendung zentral ergänzt.
- Cross-App-Konsistenz bedeutet gleiche Semantik; WPF und WinUI dürfen technisch verschiedene
  Iconbibliotheken verwenden.

