# Gemeinsamer Fachkern und frühere WPF-Oberfläche

- Dieser Ordner enthält die frühere WPF-Oberfläche sowie Fach-, Modell-, Import- und
  Servicebestandteile, die MyCoinFlow.App.WinUI weiterhin per ProjectReference nutzt.
- Ein WinUI-Arbeitspaket darf gemeinsam genutzten Fachcode nur ändern, wenn dies für sein
  fachliches Ziel zwingend ist. Datenintegrität und Kompatibilität bleiben erhalten.
- WPF-XAML, WPF-Ressourcen und BaseWindow werden nicht automatisch mit einer WinUI-Änderung
  überarbeitet oder als aktuelle Designreferenz verwendet.
- Ein ausdrücklich benanntes WPF-Wartungspaket verwendet weiterhin WPF und
  MaterialDesignThemes und erhält eigene Versions- und Prüfgrenzen.
