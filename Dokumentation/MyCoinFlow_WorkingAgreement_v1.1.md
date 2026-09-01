# MyCoinFlow - Working Agreement v1.1

## 1. Ablauf und Kommunikation

- Du beschreibst dein Anliegen in Alltagssprache (ohne Fachbegriffe).
- Ich erkläre zuerst kurz und einfach, welche Schritte nötig sind und was du erwarten kannst, bevor Code entsteht.
- Erst wenn du "go" sagst, beginne ich mit dem ersten Schritt.
- Danach arbeiten wir Schritt für Schritt - immer nur eine Methode, eine Klasse oder eine klar abgegrenzte Änderung.

## 2. Code-Umgang

- Keine bestehenden Funktionen verändern, ausser wenn dies zwingend nötig ist (z. B. für einen neuen Button oder Event).
- Wenn etwas angepasst werden muss, gebe ich dir immer den vollständigen Block (also die ganze Methode oder Klasse).
- Ich erfinde keine Variablen oder Methoden neu, sondern frage zuerst, ob sie schon existieren.
- Ich halte mich exakt an deinen bisherigen Stil, insbesondere UI-Struktur, MaterialDesign-Style, Binding-Syntax, Icons und defensive Checks.

## 3. Projektstruktur

- Neue Funktionen bekommen eigene Module oder Views, ausser es gehört logisch in einen bestehenden Service (meist DatabaseService).
- Der Code wird kompilierbar nach jedem Schritt geliefert. Wenn er nicht kompiliert, bleiben wir an dieser Stelle, bis alles sauber funktioniert.
- Ich gebe genaue Einfügehinweise, z. B. in ZuordnungDialog.xaml.cs, Methode Speichern_Click, direkt vor `DialogResult = true;`.

## 4. Zusammenarbeit

- Ich schreibe keine Alternativen oder Varianten ("entweder/oder"). Es gibt immer genau einen empfohlenen Weg.
- Kommentare im Code sind kurz und sachlich (nur zur Funktion, keine Theorie).
- Ich frage nach, wenn etwas unklar ist - lieber nachfragen als raten.
- Du entscheidest das Tempo: "go" = weiter, "stopp" = bleiben.
- Nebenschauplätze werden nicht begonnen; jedes Thema bleibt in seinem Task isoliert.

## 5. Qualität und Stil

- Alle XAML-Dateien haben den korrekten Namespace `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`.
- Icons, Buttons und Layout folgen der Datei MyCoinFlow_UI-Standard_v1.0.pdf.
- Ich respektiere bestehende Bezeichner (z. B. VonKontoId, NachKontoId, IstAktiv, NotifyLabelPropertiesChanged()).
- Alle Lösungen müssen stabil, verständlich und wartbar sein - keine Tricks oder temporären Workarounds.

## 6. Scrollbare Formulare und Scrollbar-Gutter

- Jeder vertikal scrollbare Formularbereich reserviert rechts einen festen Scrollbar-Freiraum (Scrollbar-Gutter).
- Vertikale Scrollbars dürfen niemals Eingabefelder, ComboBoxen, Listenrahmen, Beschriftungen oder Aktionsflächen überlagern.
- In WinUI wird dafür der gemeinsame `FormScrollViewerStyle` verwendet. In WPF wird ein gleichwertiger rechter Innenabstand beziehungsweise Inhaltsrand vorgesehen.
- Die Regel gilt verbindlich für jede neue oder geänderte scrollbare Maske, nicht nur für das Berichtswesen.
- Die visuelle Prüfung erfolgt bei sichtbarer Scrollbar sowie bei kleiner Fenstergröße beziehungsweise erhöhter Anzeigeskalierung.

## 7. Fensterposition, Fenstergröße und Mehrmonitorbetrieb

- Jedes eigenständige Window merkt sich beim regulären Schließen seine letzte Position sowie bei skalierbaren Fenstern seine normale Fenstergröße.
- Der Maximierungszustand wird ebenfalls gespeichert; minimierte Fenster werden nicht minimiert wiederhergestellt.
- Nicht skalierbare Dialogfenster übernehmen nur ihre letzte Position. Ihre Größe stammt weiterhin aus der aktuellen UI-Definition, damit spätere Layoutanpassungen nicht durch alte gespeicherte Werte überschrieben werden.
- Vor der Wiederherstellung wird geprüft, ob die gespeicherte Position auf einem aktuell vorhandenen Bildschirm liegt. Nach einer geänderten Monitoranordnung wird ein nicht mehr sichtbares Fenster passend auf dem Primärbildschirm zentriert.
- In WinUI erbt jedes echte Fenster verbindlich von der gemeinsamen `PersistentWindow`-Basisklasse. Die WinUI-Zustände werden getrennt von WPF gespeichert, damit sich die parallel betriebenen Versionen nicht gegenseitig überschreiben.
- Die Regel gilt verbindlich für das Hauptfenster und sämtliche bestehenden sowie künftig hinzukommenden eigenständigen Fenster.

## 8. Zusammenhängende Fachkontexte und abhängige Auswahlen

- Fachlich voneinander abhängige Bereiche dürfen nicht in getrennten Registern verborgen werden, wenn die Auswahl im übergeordneten Bereich den Inhalt eines nachgeordneten Bereichs bestimmt.
- Die aktuell wirksame Auswahlkette wird dauerhaft sichtbar dargestellt, zum Beispiel `Liegenschaft -> Einheit -> Eigentumszuordnung` beziehungsweise `Verteilschlüssel -> zugeordnete Zähler`.
- Abhängige Listen stehen räumlich zusammen. Überschrift, Kontexttext und verfügbare Aktionen benennen eindeutig, auf welchen markierten Datensatz sie wirken.
- Ein Wechsel der übergeordneten Auswahl lädt exakt dieselben abhängigen Daten und setzt dieselben Anfangsselektionen wie die bestehende WPF-Funktion. Die grafische Darstellung darf keine zusätzliche Filter-, Speicher- oder Berechnungslogik einführen.
- Technische Fremdschlüssel werden in der Oberfläche durch die fachlichen Bezeichnungen ersetzt. Falls ein verknüpfter Stammdatensatz nicht aufgelöst werden kann, bleibt die technische ID als erkennbare Rückfallanzeige sichtbar.
- Die Regel gilt verbindlich für neue und geänderte Stammdatenmasken mit bereichsübergreifenden Beziehungen.

Ende des Working Agreements v1.1.
