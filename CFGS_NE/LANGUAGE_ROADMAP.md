# CFGS Sprach-Roadmap

Stand: 2026-03-08

## Beschlossen (Prioritaet und Reihenfolge)

- [x] 1) **CFB/Binary komplett entfernen**
  - `.cfb` Ein- und Ausgabe aus CLI entfernen
  - Compile-zu-Binary (`-c`) entfernen
  - Binary-Loader/Serializer entfernen (`CFSBinary`)
  - Dazugehoerige Edgecases entfernen/anpassen

- [x] 2) **Match im Kern ausbauen**
  - Echte Pattern-Matching-Bausteine (nicht nur `==`)
  - Pattern-Bindings (z. B. Array/Dict-Bindings)
  - Guards sauber mit Pattern kombinieren

- [x] 3) **Modul-System haerten**
  - Klare Modulgrenzen/Modul-Scope
  - Mehrfachimporte robust und konfliktfrei
  - Exports/Imports konsistent und eindeutig

- [x] 4) **Funktionsaufrufe abrunden**
  - Named/Default-Args weiter stabilisieren
  - Rest/Spread-Design entscheiden und umsetzen
  - Arity- und Parameterfehler verbessern

- [x] 5) **Fehlersystem & Diagnostik**
  - Bessere Fehlermeldungen (Parser/Compiler/Runtime)
  - Verlaessliche Stacktraces
  - Einheitliche Fehlerkategorien/-texte

- [x] 6) **Destructuring allgemein**
  - `var/const` Destructuring
  - Assignment-Destructuring
  - Param-Destructuring

## Spaeter (optional)

- Weitere Komfort-/Tooling-Themen nach Bedarf.
