# CFGS Kern Hardening Roadmap

Stand: 2026-03-08

## Ziel

Offene Kernpunkte aus der Tiefenanalyse priorisiert abarbeiten:
- zuerst sicherheits-/semantik-kritische Luecken,
- danach Konsistenz im OOP-Modell,
- zum Schluss Cleanup/Diagnostik-Qualitaet.

## Reihenfolge

- [x] P0 (Kritisch) `new`-Slot Runtime-Hardening
  - Problem:
    - Compiler behandelt `new` als reservierten Runtime-Member.
    - VM blockiert bei Alias-Schreibzugriff aktuell nur `__*`, nicht `new`.
  - Aufgabe:
    - Reserved-Policy in VM und Compiler angleichen (`new` ebenfalls Runtime-reserved).
    - Alias-Schreibzugriffe auf `type["new"]`/`Class["new"]` blocken.
  - DoD:
    - Runtime kann `new` nicht mehr ueber Alias ueberschreiben.
    - Neue Edgecases fuer positive/negative Faelle vorhanden.
  - Umsetzung:
    - VM-Reserved-Check erweitert (`new` jetzt reserviert).
    - Edgecases hinzugefuegt:
      - `223_constructor_alias_read_valid`
      - `481_reserved_ctor_alias_instance_fail`
      - `482_reserved_ctor_alias_static_fail`

- [x] P1 (Mittel) Override-Visibility-Regeln fixieren
  - Problem:
    - Override-Validierung prueft Kind/Arity, aber nicht Access-Level.
  - Aufgabe:
    - Regel festlegen (z. B. C#-kompatibel: Sichtbarkeit darf nicht "enger" werden).
    - Compile-Time-Pruefung fuer Overrides erweitern.
  - DoD:
    - Inkompatible Sichtbarkeits-Overrides liefern klaren Compilerfehler.
    - Edgecases fuer valid/invalid Overrides vorhanden.
  - Umsetzung:
    - Compile-Time-Check hinzugefuegt: Override-Sichtbarkeit darf nicht enger als im Basistyp sein.
    - Neue Edgecases:
      - `224_override_visibility_compatibility_valid`
      - `483_override_visibility_narrower_instance_fail`
      - `484_override_visibility_narrower_static_fail`

- [x] P2 (Mittel) Constructor-Accessibility sauber modellieren
  - Problem:
    - `new` ist als Konstruktor-Einstieg nicht in Visibility-Metadaten modelliert.
    - `private/protected init` steuert externe Instanziierung aktuell nicht explizit.
  - Aufgabe:
    - Konstruktor-Access-Semantik festlegen (inkl. `private/protected`).
    - Metadaten/Checks fuer Instanziierung (`new`) entsprechend erweitern.
  - DoD:
    - Instanziierung folgt den festgelegten Access-Regeln reproduzierbar.
    - Edgecases fuer externe/abgeleitete/interne Konstruktion vorhanden.
  - Umsetzung:
    - Konstruktor-Sichtbarkeit auf `new` modelliert (abgeleitet aus `init`-Visibility, sonst `public`).
    - Compile-Time-Check fuer `new Type(...)` eingebaut.
    - Static-Visibility-Metadaten enthalten jetzt auch `new`, Runtime-Visibility greift dadurch konsistent.
    - Neue Edgecases:
      - `225_constructor_access_visibility_valid`
      - `485_constructor_private_external_fail`
      - `486_constructor_protected_external_fail`

- [x] P3 (Leicht) Fehlerkonsistenz im VM-Kern
  - Problem:
    - Einzelne Stellen werfen generische `Exception`.
    - Zwei Runtime-Fehlertexte haben kaputtes Quote-Ende.
  - Aufgabe:
    - Generische Exception auf VM/Diagnostics-konforme Fehler umstellen.
    - Fehltexte korrigieren.
  - DoD:
    - Einheitliche Fehlerkategorie und saubere Meldungen an allen betroffenen Stellen.
  - Umsetzung:
    - `LoadFunctions` wirft nun `VMException` statt generischer `Exception`.
    - Zwei fehlerhafte Runtime-Texte fuer `undefined variable` korrigiert.
    - Neue Edgecases:
      - `487_delete_missing_index_var_fail`
      - `488_delete_missing_var_fail`

- [x] P4 (Leicht) Import-Cache-Cleanup
  - Problem:
    - `_importedHashes` wird befuellt, aber derzeit nicht als aktive Entscheidungslogik genutzt.
  - Aufgabe:
    - Entweder echte Nutzung mit klarer Semantik einbauen oder Feld entfernen.
  - DoD:
    - Kein toter Zustand mehr im Import-Hash-Pfad; Verhalten dokumentiert.
  - Umsetzung:
    - Toten `_importedHashes`-Pfad entfernt (Feld, Constructor-Parameter, Hash-Berechnung, Add-Aufrufe).
    - Import-Caching bleibt klar ueber `_astByImportKey` + `_importStack` (Cache/Idempotenz/Zyklen).

## Teststrategie pro Punkt

- Vor jedem Punkt:
  - relevante bestehende `_edgecases` selektiv laufen lassen.
- Nach jedem Punkt:
  - neue/angepasste Edgecases gruen,
  - danach komplette Suite:
    - `powershell -ExecutionPolicy Bypass -File _edgecases\\run_all_edgecases.ps1 -SkipBuild`

## Changelog

- 2026-03-08: Roadmap initial angelegt.
- 2026-03-08: P0 abgeschlossen (`new` Runtime-Hardening + Edgecases 223/481/482).
- 2026-03-08: P1 abgeschlossen (Override-Visibility-Kompatibilitaet + Edgecases 224/483/484).
- 2026-03-08: P2 abgeschlossen (Constructor-Accessibility + Edgecases 225/485/486).
- 2026-03-08: P3 abgeschlossen (VM-Fehlerkonsistenz + Edgecases 487/488).
- 2026-03-08: P4 abgeschlossen (Import-Cache-Cleanup: `_importedHashes` entfernt).
