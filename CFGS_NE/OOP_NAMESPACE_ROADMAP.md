# CFGS OOP/Namespace Roadmap

Stand: 2026-03-08

## Ziel

Klassen- und Namespace-Verhalten im Kern konsistent, vorhersagbar und testbar machen.

## Ist-Stand (kurz)

- Klassen mit Vererbung, `super`, `type`, statischen Membern, Enums und Nested Classes sind vorhanden.
- `outer` fuer Nested Classes ist vorhanden.
- Namespace-Import existiert aktuell nur als `import * as Alias from "mod.cfs"` (Alias-Objekt), nicht als echtes Sprachkonstrukt `namespace ... {}`.
- Mehrere OOP-Fehler werden erst zur Runtime erkannt (z. B. falscher `super`/`this`/`outer`-Kontext).

## Reihenfolge (Punkt fuer Punkt)

- [x] 1) Echte Namespaces im Sprachkern
  - Syntax einfuehren: `namespace A { ... }` und `namespace A.B { ... }`.
  - Qualifizierte Aufloesung fuer Klassen/Funktionen/Enums (`A.B.Foo`).
  - Import-Namespaces und deklarierte Namespaces sauber zusammenfuehren.
  - DoD: Parser+Compiler+VM support + Edgecases fuer valid/invalid Namespace-Scopes.

- [x] 2) Namensauflosung fuer OOP haerten
  - Einheitliche Aufloesungsreihenfolge festlegen (locals -> receiver/member -> namespace -> global).
  - Ambigue Symbole frueh als Compilerfehler melden.
  - DoD: Edgecases fuer Shadowing, Mehrdeutigkeiten und qualifizierte Zugriffe.

- [x] 3) `this`/`super`/`type`/`outer` statisch validieren
  - Fehlgebrauch (ausserhalb Klasse, statischer Kontext, ungueltiger Nested-Kontext) bereits im Compiler melden.
  - Einheitliche Diagnose-Texte fuer diese 4 Receiver.
  - DoD: bestehende Runtime-Fehlerfaelle als Compiler-Fehler abdecken, wo statisch moeglich.

- [x] 4) Vererbung und Overrides absichern
  - Ueberschreiben vs. neues Member klar unterscheiden (Regelwerk + Diagnose).
  - Optionales `override`-Verhalten vorbereiten (mindestens Kompatibilitaetscheck bei vorhandenen Basismethoden).
  - DoD: Edgecases fuer Signaturabweichung, statisch/instanz Mismatch, ungueltiges Override.

- [x] 5) Konstruktorfluss robuster machen
  - Basiskonstruktor-Aufruf (Arity/Named Args) frueh pruefen, wenn Typ bekannt.
  - Nested-Konstruktor/`__outer`-Pfad robust und klar diagnostizieren.
  - DoD: Edgecases fuer Base-Args, fehlende/new-Constructor-Pfade, Nested-Konstruktion.

- [x] 6) Memberzugriffe und Objektinitialisierung haerten
  - Regeln fuer Initializer (`new T(){...}`) bei unbekannten Feldern festlegen (strict/compat).
  - Konsistente Fehlertexte bei invaliden Instanz-/Static-Memberzugriffen.
  - DoD: Edgecases fuer unknown field, read-only/reserved member, static-vs-instance Zugriff.

- [x] 7) OOP-Testmatrix + Samples/Tutorials nachziehen
  - `_edgecases` um OOP/Namespace-Matrix erweitern (positive + negative).
  - `Samples/general_Tests` und `Tutorial/Introduction.md` auf neuen Kernstand bringen.
  - DoD: Edgecase-Suite gruen, Samples laufen, Tutorial deckt neue Semantik ab.

## Umsetzungsmodus

- Wir arbeiten die Punkte in dieser Reihenfolge ab.
- Nach jedem Punkt: Code + Edgecases + kurzer Changelog in dieser Datei.

## Changelog

- 2026-03-08: Punkt 1 abgeschlossen.
  - Neues Sprach-Keyword: `namespace`.
  - Neue Syntax aktiv: `namespace A { ... }` und `namespace A.B { ... }`.
  - Namespace-Member (var/const/func/class/enum) werden an den Namespace gebunden, ohne globale Leaks.
  - Namespace-Body ist gehaertet (`import`/`export`/nested namespace in Body werden klar abgewiesen).
  - Neue Edgecases:
    - `_edgecases/215_namespace_declarations.cfs`
    - `_edgecases/439_namespace_member_duplicate_fail.cfs`
    - `_edgecases/440_namespace_import_inside_body_fail.cfs`
    - `_edgecases/441_namespace_reserved_name_fail.cfs`

- 2026-03-08: Punkt 2 abgeschlossen.
  - Namespace-Root-Konflikte werden frueh als Parserfehler gemeldet
    (z. B. `func X` + `namespace X {}`, sowie Import-Alias + `namespace Alias {}`).
  - Implizite OOP-Namensauflosung wurde gehaertet:
    - Reihenfolge bleibt: locals -> implizite Member.
    - Ambigue implizite Memberauflosung (gleichzeitig Instanz+Static sichtbar) ist jetzt Compilerfehler.
  - Neue Edgecases:
    - `_edgecases/216_name_resolution_order.cfs`
    - `_edgecases/442_ambiguous_member_resolution_fail.cfs`
    - `_edgecases/443_namespace_root_conflict_fail.cfs`
    - `_edgecases/444_namespace_import_alias_conflict_fail.cfs`

- 2026-03-08: Punkt 3 abgeschlossen.
  - Receiver-Nutzung wird jetzt statisch validiert:
    - `this`: nur in Instanzmethoden.
    - `type`: nur in Klassenmethoden.
    - `super`: nur in Klassenmethoden mit Basisklasse.
    - `outer`: nur in verschachtelten Instanzmethoden.
  - Receiver-Zuweisungen (`this = ...`, etc.) werden als Compilerfehler abgewiesen.
  - Receiver in lokalen/nested Funktionen ohne Receiver-Kontext wird frueh erkannt.
  - Neue Edgecases:
    - `_edgecases/217_receiver_valid_contexts.cfs`
    - `_edgecases/445_this_outside_method_fail.cfs`
    - `_edgecases/446_type_outside_method_fail.cfs`
    - `_edgecases/447_super_without_base_fail.cfs`
    - `_edgecases/448_outer_non_nested_fail.cfs`
    - `_edgecases/449_outer_static_nested_fail.cfs`
    - `_edgecases/450_receiver_assignment_fail.cfs`
    - `_edgecases/451_receiver_in_local_function_fail.cfs`

- 2026-03-08: Punkt 4 abgeschlossen.
  - Vererbte Member werden im Compiler jetzt gegen Basisklassen geprueft:
    - Kind-Mismatch wird als Compilerfehler abgewiesen (z. B. Feld vs. Methode).
    - Static/Instanz-Mismatch wird als Compilerfehler abgewiesen.
    - Methoden-Override muss arity-shape-kompatibel sein
      (Mindestargumente, Restparameter, Parameteranzahl).
  - Regel fuer Namensauflosungstest angepasst:
    - `442_ambiguous_member_resolution_fail` erwartet nun den fruehen Override-Fehler.
  - Neue Edgecases:
    - `_edgecases/218_override_compatibility.cfs`
    - `_edgecases/452_override_signature_mismatch_fail.cfs`
    - `_edgecases/453_override_kind_mismatch_field_vs_method_fail.cfs`
    - `_edgecases/454_override_kind_mismatch_static_instance_fail.cfs`
    - `_edgecases/455_override_kind_mismatch_static_method_vs_instance_fail.cfs`

- 2026-03-08: Punkt 5 abgeschlossen.
  - Basiskonstruktor-Aufrufe werden fuer bekannte Basisklassen jetzt bereits im Compiler geprueft:
    - Arity/Mindestargumente
    - Named-Args (unknown/duplicate)
    - `positional after named`
    - Rest-Parameter als Named-Arg
  - Base-Argumentlisten in `class Child() : Base(...)` unterstuetzen jetzt Named- und Spread-Args.
  - Nested-Konstruktor-Diagnose wurde gehaertet:
    - fehlendes `__outer` liefert jetzt einen spezifischen Runtime-Fehlertext.
  - Neue Edgecases:
    - `_edgecases/219_constructor_flow.cfs`
    - `_edgecases/456_base_ctor_unknown_named_arg_fail.cfs`
    - `_edgecases/457_base_ctor_insufficient_args_fail.cfs`
    - `_edgecases/458_base_ctor_positional_after_named_fail.cfs`
    - `_edgecases/459_base_ctor_named_rest_fail.cfs`
    - `_edgecases/460_nested_constructor_missing_outer_fail.cfs`

- 2026-03-08: Punkt 6 abgeschlossen.
  - Objektinitialisierung gehaertet:
    - `new T(){...}` prueft reservierte Membernamen immer.
    - Bei bekannten Klassen prueft der Compiler Initializer-Member strikt gegen bekannte Instanzmember.
  - Explizite Memberzugriffe gehaertet:
    - Static-vs-instance Fehlzugriffe werden fuer bekannte Klassen/Receiver (`this`/`type`/`super`) frueh als Compilerfehler gemeldet.
  - Reserved-Member-Assignments gehaertet:
    - Explizite Zuweisungen auf reservierte Runtime-Membernamen werden als Compilerfehler abgewiesen.
  - Runtime-Texte fuer ungueltige Instanzmember-Zugriffe vereinheitlicht.
  - Neue Edgecases:
    - `_edgecases/461_initializer_unknown_member_fail.cfs`
    - `_edgecases/462_initializer_reserved_member_fail.cfs`
    - `_edgecases/463_this_static_member_access_fail.cfs`
    - `_edgecases/464_type_instance_member_access_fail.cfs`
    - `_edgecases/465_class_static_access_instance_member_fail.cfs`
    - `_edgecases/466_instance_assign_reserved_member_fail.cfs`
    - `_edgecases/467_static_assign_reserved_member_fail.cfs`

- 2026-03-08: Punkt 7 abgeschlossen.
  - OOP/Namespace-Matrix erweitert:
    - neuer positiver Matrix-Fall fuer Memberzugriffe + Initializer:
      `_edgecases/220_member_access_and_initializer_valid.cfs`
    - Runner erweitert (`_edgecases/run_all_edgecases.ps1`) und gesamte Edge-Suite gruen.
  - Samples aktualisiert:
    - neues Gesamtbeispiel:
      `Samples/general_Tests/feature_05_oop_namespace_hardening.cfs`
  - Tutorial aktualisiert:
    - `Tutorial/Introduction.md` um OOP/Namespace-Haertungsregeln erweitert
      (Namespaces, Receiver-Regeln, Overrides, Konstruktorfluss, Initializer-Regeln).
