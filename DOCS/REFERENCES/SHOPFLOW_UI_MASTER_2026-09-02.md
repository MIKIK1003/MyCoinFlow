# ShopFlow-UI-Master für MyCoinFlow

**Quellstand:** ShopFlow-Verträge und visuelle Referenzen vom 2. September 2026  
**Zweck:** reproduzierbare Designreferenz für die schrittweise WPF-Angleichung

## Referenzbilder

- debitoren-filterleiste.png: Such-/Status-/Schnellansicht-/Filtergrammatik
- reparatur-filterleiste.png: dieselbe Leiste in einem Statusworkflow
- debitoren-funktionsleiste.png: dichte gruppierte Funktionsleiste
- reparatur-funktionsleiste.png: reduzierte Leiste bei kleinerem Aktionsumfang

## Übertragungsregel

Die Bilder definieren Hierarchie, Gruppierung, Abstände, vollständige Texte, Aktionssemantik
und ruhige Flächenwirkung. Pixelgenaue WinUI-Implementierung, ShopFlow-Funktionen und Segoe-
Glyphcodes werden nicht kopiert. MyCoinFlow setzt dasselbe Muster mit WPF,
MaterialDesign-PackIcons und seinem Fachkontext um.

Debitoren ist der bevorzugte Master für Transaktionen und DMS. Vermietung liefert die
Listen-/Detailgrammatik für Bestände und Verträge. Reparatur zeigt die Reduktion auf einen
echten Statusworkflow.

