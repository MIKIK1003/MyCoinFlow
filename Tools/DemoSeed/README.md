# DemoSeed – Pseudo-Datenbank für Marketing-Screenshots

Erzeugt eine realistische, aber **komplett erfundene** Demo-Datenbank
(`MyCoinFlowDemo`) für Screenshots: Musterfamilie Anna & Marc Muster,
16 Monate Buchungen (Apr 2025 – Jul 2026), Budgets 2025/2026, Depot mit
Kurshistorie, Haushalt-Inventar und Demo-STWE.

## Ablauf

1. **Demo-Mandant in der App anlegen** (einmalig):
   App starten → Mandantenverwaltung → neue Datenbank `MyCoinFlowDemo`
   aus Template anlegen. Beim ersten Start einen Demo-Login registrieren
   (z. B. `demo`), damit für Screenshots kein echter Benutzername sichtbar ist.
2. **Module einmal öffnen** (Vermögen, Haushalt, STWE), damit die App die
   Modul-Tabellen anlegt. App danach schliessen.
3. **Seed einspielen:**
   ```
   sqlcmd -S .\SQLEXPRESS -E -d MyCoinFlowDemo -i demo_seed.sql
   ```
4. App starten, Mandant `MyCoinFlowDemo` wählen → Screenshots machen.
   Zurück zur echten DB: in der App einfach wieder den eigenen Mandanten aktivieren.

## Sicherheit

- Das Skript prüft `DB_NAME()` und **bricht ab**, wenn es nicht auf
  `MyCoinFlowDemo` läuft – ein versehentlicher Lauf gegen die echte DB
  ist damit ausgeschlossen.
- Es löscht vor dem Einspielen alle Daten der Demo-DB → beliebig wiederholbar.
- Modul-Sektionen (Vermögen/Haushalt/STWE) werden übersprungen, falls die
  Tabellen noch fehlen (Hinweis im Output; Modul öffnen und erneut ausführen).

## Anpassen

Daten ändern → `make_demo_seed.py` bearbeiten und neu generieren:
```
python make_demo_seed.py
```
Der Zufalls-Seed ist fixiert (reproduzierbare Ergebnisse).
