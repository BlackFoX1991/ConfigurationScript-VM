# CFGS Parser Compiler VM Refactor Roadmap

Stand: 2026-04-07

## Ziel

Den Sprachkern von CFGS so refactoren, dass Parser, Compiler und VM wieder klar getrennte
Verantwortlichkeiten haben, ohne das aktuelle Sprachverhalten zu verlieren.

Die Roadmap ist absichtlich in kleine, abhakbare Schritte zerlegt. Jeder Schritt soll in einem
kleinen bis mittleren PR umsetzbar sein und einen klaren Rueckweg haben.

## Warum diese Roadmap noetig ist

Aktuell sind die Kernkomponenten funktional stark, aber strukturell zu dicht gekoppelt:

- `CFGS_NE/Analytic/Core/Parser.cs`
  - mischt Syntax, Import-I/O, HTTP-Import, DLL-Aktivierung, Namespace-Materialisierung und
    Param-Lowering.
- `CFGS_NE/VMCore/Compiler.cs`
  - mischt Symbolaufbau, semantische Validierung, Klassenmodell, Member-Checks und Bytecode-Emission.
- `CFGS_NE/VMCore/VM.cs`
  - mischt Interpreter-Loop, Async-Runtime, Numeric Ops, Collections, Object-Model, Visibility,
    Intrinsics, JSON/Printing und Lifecycle-Handling.

Ziel ist keine "Neuschreibung", sondern ein kontrollierter Umbau in Passes.

## Leitlinien

- [ ] Verhalten zuerst erhalten, dann Struktur verbessern.
- [ ] Keine grosse IR-Neuschreibung am Anfang.
- [ ] Keine gleichzeitige Vollsanierung von Parser und VM.
- [ ] Jeder Schritt muss separat buildbar und testbar bleiben.
- [ ] Bestehende Edgecases muessen waehrend des gesamten Umbaus gruen bleiben.
- [ ] Neue Strukturen erst einfuehren, dann Altcode schrittweise entkernen.

## Zielarchitektur

Die finale Zielstruktur ist ungefaehr diese:

```text
CFGS_NE/
  Frontend/
    Lexing/
      Lexer.cs
      TokenCursor.cs
    Syntax/
      Parser.cs
      ParserContext.cs
      Parsing/
        ImportSyntaxParser.cs
        DeclarationParser.cs
        StatementParser.cs
        ExpressionParser.cs
        PatternParser.cs
    Modules/
      ImportResolver.cs
      ModuleGraphBuilder.cs
      SourceResolver.cs
    Semantics/
      SymbolIndex.cs
      TopLevelValidator.cs
      NamespaceBinder.cs
      ClassCatalog.cs
      InterfaceCatalog.cs
      MemberAccessRules.cs
    Lowering/
      ParamLowerer.cs
      NamespaceLowerer.cs
      SugarLowerer.cs
  Codegen/
    CompilationPipeline.cs
    CompilationContext.cs
    BytecodeEmitter.cs
    Emitters/
      StmtEmitter.cs
      ExprEmitter.cs
      PatternEmitter.cs
      ClassEmitter.cs
      FlowEmitter.cs
      CallEmitter.cs
  Runtime/
    VmState.cs
    ExecutionEngine.cs
    CallRuntime.cs
    AsyncRuntime.cs
    ValueOps.cs
    CollectionOps.cs
    ObjectRuntime.cs
    ExceptionRuntime.cs
    BindingRuntime.cs
    ValuePrinter.cs
```

Hinweis:
- Die exakten Dateinamen koennen leicht angepasst werden.
- Wichtig ist die Trennung der Verantwortlichkeiten, nicht der Name an sich.

## Abnahmeregel pro Phase

Jede Phase gilt nur dann als abgeschlossen, wenn alle Punkte erfuellt sind:

- [ ] `dotnet build -v minimal CFGS_VM.sln` ist gruen.
- [ ] Selektive Edgecases fuer die geaenderten Bereiche sind gruen.
- [ ] Voller Lauf `powershell -ExecutionPolicy Bypass -File .\\CFGS_NE\\_edgecases\\run_all_edgecases.ps1 -SkipBuild` ist gruen.
- [ ] Keine funktionale Regression in Doku-Samples, REPL oder LSP.
- [ ] Altcode wurde nicht "parallel weiterleben" gelassen, wenn der neue Pfad produktiv ist.

## Phase 0 - Baseline und Sicherheitsnetz

- [x] `Roadmap.md` als Fuehrungsdokument pflegen.
- [x] Vor dem ersten Refactor die Kernpfade markieren:
  - Parser Importpfad
  - Parser Ausdrucksparser
  - Compiler Semantikpfad
  - Compiler Bytecode-Emission
  - VM Dispatch-Loop
  - VM Async-Pfad
  - VM Object/Visibility-Pfad
- [x] Fuer jeden der grossen Bereiche einen minimalen "Smoke-Cluster" von Edgecases notieren.
- [ ] Optional: kleine interne Notizdatei `artifacts/refactor_notes.md` oder aehnlich fuer Migrationserkenntnisse.

DoD:
- [x] Es ist fuer jede Folgephase klar, welche Testfaelle den Bereich absichern.

Empfohlene Smoke-Cluster:
- Parser/Syntax:
  - `101_core_expressions`
  - `102_control_flow_and_match`
  - `205_default_and_named_args`
  - `212_destructuring_general`
  - `215_namespace_declarations`
- Compiler/Semantik:
  - `218_override_compatibility`
  - `221_namespace_oop_hardening`
  - `239_interfaces_and_implements`
  - `452_override_signature_mismatch_fail`
  - `468_namespace_override_validation_fail`
- VM/Runtime:
  - `226_async_await_nonblocking_collections`
  - `230_async_hot_start_call_order`
  - `236_destroy_builtin`
  - `237_using_statement`
  - `243_async_shared_collection_aliases`

## Phase 1 - Parser nur aufspalten, Verhalten nicht aendern

Ziel:
- `Parser.cs` in mehrere Dateien schneiden, ohne Grammatik oder Verhalten zu aendern.

Schritte:
- [x] `Parser` als `partial class` vorbereiten.
- [ ] Dateischnitt einfuehren:
  - [x] `Parser.cs` als Kernfile behalten
  - [x] `Parser.Imports.cs`
  - [x] `Parser.Statements.cs`
  - [x] `Parser.Expressions.cs`
  - [x] `Parser.FunctionParams.cs`
- [x] Gemeinsame Felder und Basishelfer im Kernfile konzentrieren.
- [x] Keine Methodensignaturen veraendert, solange der Cut lief.
- [x] Null funktionale Unterschiede zwischen alter und neuer Struktur sichergestellt.

DoD:
- [x] Parser ist auf mehrere Dateien verteilt.
- [x] Build, Parser-Smokes und volle Edge-Suite sind nach dem Split gruen.

Nicht in dieser Phase:
- Kein `TokenCursor`.
- Kein neues AST-Modell.
- Keine Modul-/Import-Entkopplung.

## Phase 2 - TokenCursor und ParserContext einfuehren

Ziel:
- Cursor- und Kontextlogik aus dem Parser-Textfluss herausloesen.

Neue Zielelemente:
- `Frontend/Lexing/TokenCursor.cs`
- `Frontend/Syntax/ParserContext.cs`

Schritte:
- [x] `TokenCursor` einfuehren fuer:
  - [x] Current
  - [x] Next
  - [x] Advance
  - [x] Eat
  - [ ] optionale `Match`/`Expect`-Hilfen
- [x] Parser intern von `_current` und `_next` auf Cursor abstrahieren.
- [x] `ParserContext` einfuehren fuer:
  - [x] Function depth
  - [x] Async function depth
  - [x] Function or class depth
  - [x] Loop depth
  - [x] Out block depth
  - [x] Recursion depth
- [x] Fehlermeldungen unveraendert halten.

DoD:
- [x] Parser-Zustandsverwaltung ist nicht mehr verstreut ueber rohe Felder.
- [x] Alle Parser-Tests und Edgecases bleiben gruen.

Risiko:
- Kleine Positions- oder Kontextregressionen.

Spezielle Regressionstests:
- [x] `499_await_without_async_fail`
- [x] `500_yield_without_async_fail`
- [x] `503_await_position_fail`
- [x] `504_async_func_expr_position_fail`

## Phase 3 - Importsyntax von Importauflosung trennen

Ziel:
- Der Parser erzeugt nur noch Syntax bzw. AST fuer Imports.
- Dateisystem, HTTP und DLL-Handling verlassen den Parser.

Neue Zielelemente:
- `Frontend/Modules/ImportResolver.cs`
- `Frontend/Modules/SourceResolver.cs`
- `Frontend/Modules/ModuleGraphBuilder.cs`

Schritte:
- [ ] Importformen explizit als AST/Syntaxobjekte modellieren, falls noch nicht vorhanden.
- [x] `TryHandleDllImport`, `GetImports`, `ResolveImportPath`, HTTP-Download und Import-Cache aus dem Parser herausziehen.
- [ ] Parser darf nur noch:
  - `import "x"`
  - `import * as Ns from "x"`
  - `import { a as b } from "x"`
  - `import X from "x"`
    syntaktisch erkennen.
- [ ] `ModuleGraphBuilder` baut den Importgraphen.
- [x] `ImportResolver` materialisiert Module und Plugins ausserhalb des Ausdrucks-/Statement-Parsers.
- [x] Header-Regeln, Zyklen, Selbstimporte und Mehrfachimporte ueber den Modulpfad absichern.

DoD:
- [ ] Parser kann ohne Dateisystem-/HTTP-/DLL-Seiteneffekte arbeiten.
- [ ] Modulverhalten bleibt identisch.

Regressionstests:
- [x] `207_module_exports_imports`
- [x] `209_module_multiple_import_idempotent`
- [x] `210_module_transitive_reimport`
- [x] `401_import_header_rules_fail`
- [x] `403_import_cycle_fail`
- [x] `413_import_named_dll_fail`
- [x] `414_import_missing_dll_fail`
- [x] `494_import_invalid_dll_format_fail`

## Phase 4 - Namespace- und Param-Lowering aus dem Parser ziehen

Ziel:
- Parser erzeugt semantisch rohe Konstrukte.
- Synthetische Alias-Statements und Default-/Destructure-Initialisierer entstehen erst im Lowering.

Neue Zielelemente:
- `Frontend/Lowering/ParamLowerer.cs`
- `Frontend/Lowering/NamespaceLowerer.cs`
- optional `Frontend/Lowering/SugarLowerer.cs`

Schritte:
- [x] `ParseFunctionParamsWithDefaults()` so umbauen, dass es keine Statements mehr erzeugt.
- [x] Synthetische Param-Namen weiter erlauben, aber in einen dedizierten Lowering-Schritt verschieben.
- [x] Namespace-Alias-Erzeugung aus dem Parser entfernen.
- [x] Default-Parameter-Initialisierung, Destructure-Param-Bindung und Namespace-Alias-Materialisierung in Lowering-Passes uebernehmen.
- [ ] Lowering-Ergebnis explizit dokumentieren.

DoD:
- [x] Parser erzeugt keine "Ausfuehrungs-Statements" als Nebenprodukt syntaktischer Analyse.
- [x] Default-/Destructure-/Namespace-Verhalten bleibt unveraendert.

Regressionstests:
- [x] `205_default_and_named_args`
- [x] `211_rest_and_spread_calls`
- [x] `212_destructuring_general`
- [x] `213_foreach_destructuring`
- [x] `215_namespace_declarations`
- [x] `439_namespace_member_duplicate_fail`

## Phase 5 - Compiler in Passes zerlegen

Ziel:
- `Compiler` wird von einer Grossklasse zu einer kleinen Pipeline-Orchestrierung.

Neue Zielelemente:
- `Codegen/CompilationPipeline.cs`
- `Codegen/CompilationContext.cs`
- `Analytic/Semantics/SymbolIndex.cs`
- `Analytic/Semantics/TopLevelValidator.cs`
- `Analytic/Semantics/ClassCatalog.cs`
- `Analytic/Semantics/InterfaceCatalog.cs`
- `Analytic/Semantics/ClassSemanticValidator.cs`
- `Analytic/Semantics/MemberAccessRules.cs`

Schritte:
- [x] `Compiler.Compile()` auf reine Orchestrierung reduzieren.
- [ ] Pass 1: Symbolindex
  - [x] Top-Level-Namen
  - [x] Qualified class/interface names
  - [x] Namespace-Sicht
  - [ ] Exportoberflaechen
- [x] Pass 2: Klassenzusammenhang
  - [x] Vererbungsordnung
  - [x] Basistypauflosung
  - [x] Interface-Vertraege
- [x] Pass 3: Semantikpruefungen
  - [x] Override-Kompatibilitaet
  - [x] Constructor-Aufrufe
  - [x] Interface-Implementierung
  - [x] Visibility / Memberaccess
- [x] Pass 4: Bytecode-Emission bleibt zunaechst noch im alten Compiler, aber mit sauberer Eingabe aus den Vorpasses.

DoD:
- [ ] Semantische Fehler kommen nicht mehr aus Bytecode-Emission, wenn sie vorher pruefbar sind.
- [x] Der Compiler ist in klar erkennbare, isolierbare Passes getrennt.

Regressionstests:
- [x] `215_namespace_declarations`
- [x] `221_namespace_oop_hardening`
- [x] `218_override_compatibility`
- [x] `220_member_access_and_initializer_valid`
- [x] `224_override_visibility_compatibility_valid`
- [x] `225_constructor_access_visibility_valid`
- [x] `239_interfaces_and_implements`
- [x] `409_self_inheritance_fail`
- [x] `439_namespace_member_duplicate_fail`
- [x] `452_override_signature_mismatch_fail`
- [x] `456_base_ctor_unknown_named_arg_fail`
- [x] `461_initializer_unknown_member_fail`
- [x] `462_initializer_reserved_member_fail`
- [x] `463_this_static_member_access_fail`
- [x] `464_type_instance_member_access_fail`
- [x] `465_class_static_access_instance_member_fail`
- [x] `468_namespace_override_validation_fail`
- [x] `469_namespace_static_access_instance_member_fail`
- [x] `472_reserved_ctor_param_fail`
- [x] `473_private_member_access_fail`
- [x] `474_private_static_access_fail`
- [x] `475_protected_access_outside_fail`
- [x] `476_protected_static_access_outside_fail`
- [x] `483_override_visibility_narrower_instance_fail`
- [x] `485_constructor_private_external_fail`
- [x] `508_interface_visibility_fail`
- [x] `509_interface_extends_class_fail`

## Phase 6 - Bytecode-Emission auf Emitter aufteilen

Ziel:
- Statement-, Ausdrucks-, Pattern- und Klassenemission aus dem Monolithen schneiden.

Neue Zielelemente:
- `Codegen/BytecodeEmitter.cs`
- `Codegen/BytecodeEmissionContext.cs`
- `Codegen/Emitters/StmtEmitter.cs`
- `Codegen/Emitters/ExprEmitter.cs`
- `Codegen/Emitters/PatternEmitter.cs`
- `Codegen/Emitters/ClassEmitter.cs`
- `Codegen/Emitters/FlowEmitter.cs`
- `Codegen/Emitters/CallEmitter.cs`

Schritte:
- [x] Gemeinsamen Emissionskontext kapseln:
  - instruction list
  - locals
  - break/continue patch lists
  - current class / receiver context
  - async function depth
- [x] `CompileStmt` in `StmtEmitter` zerlegen.
- [x] `CompileExpr` in `ExprEmitter` zerlegen.
- [x] Match-/Destructure-Emission nach `PatternEmitter`.
- [x] Klassen-/Member-/Initializer-Emission nach `ClassEmitter`.
- [x] Schleifen, `try`, `using`, `defer` nach `FlowEmitter`.
- [x] Call- und Argumentverarbeitung nach `CallEmitter`.

DoD:
- [ ] Die grossen Methoden `CompileStmt` und `CompileExpr` sind nicht mehr zentrale Sammelstellen.
- [ ] Emissionsregeln sind dateiweise lokalisiert.

Regressionstests:
- [x] `102_control_flow_and_match`
- [x] `201_functions_and_closures`
- [x] `204_out_and_await`
- [x] `205_default_and_named_args`
- [x] `206_match_guards`
- [x] `208_match_patterns_and_bindings`
- [x] `211_rest_and_spread_calls`
- [x] `212_destructuring_general`
- [x] `213_foreach_destructuring`
- [x] `214_foreach_index_value_pairs`
- [x] `202_classes_enums_inheritance`
- [x] `203_nested_classes_and_object_init`
- [x] `219_constructor_flow`
- [x] `220_member_access_and_initializer_valid`
- [x] `221_namespace_oop_hardening`
- [x] `223_constructor_alias_read_valid`
- [x] `225_constructor_access_visibility_valid`
- [x] `227_yield_statement`
- [x] `230_async_hot_start_call_order`
- [x] `237_using_statement`
- [x] `238_defer_statement`
- [x] `301_try_throw_finally`
- [x] `405_break_outside_loop_fail`
- [x] `406_continue_outside_loop_fail`
- [x] `422_named_arg_duplicate_fail`
- [x] `423_named_arg_positional_after_named_fail`
- [x] `424_named_arg_unknown_fail`
- [x] `431_spread_non_array_fail`
- [x] `432_named_rest_param_fail`
- [x] `433_spread_after_named_fail`
- [x] `239_interfaces_and_implements`
- [x] `204_out_and_await`
- [x] `237_using_statement`
- [x] `238_defer_statement`

## Phase 7 - Klassisches Bound-Modell pruefen, aber erst jetzt

Ziel:
- Nach der Pass-Trennung entscheiden, ob ein `BoundProgram` echten Mehrwert bringt.

Entscheidungspunkt:
- [ ] Wenn Semantikdaten noch zu oft aus rohem AST gezogen werden, `Bound*`-Layer einfuehren.
- [ ] Wenn die jetzige Pass-Struktur stabil und klar genug ist, kein Bound-Layer erzwingen.

Falls eingefuehrt:
- [ ] `BoundProgram`
- [ ] `BoundFunction`
- [ ] `BoundClass`
- [ ] `BoundExpr`
- [ ] `BoundStmt`

Kriterien fuer "ja":
- Wiederholte Namensaufloesung an mehreren Stellen
- Wiederholte Typ-/Memberauflosung an mehreren Stellen
- Emission braucht bereits stark normalisierte Semantik

Kriterien fuer "nein":
- Passstruktur ist schon schlank
- Emission arbeitet mit dem AST weiterhin klar genug

## Phase 8 - VM nur aufspalten, Dispatch beibehalten

Ziel:
- Die VM organisatorisch trennen, ohne sofort den Opcode-Dispatch neu zu entwerfen.

Neue Zielelemente:
- `Runtime/VmState.cs`
- `Runtime/ExecutionEngine.cs`
- `Runtime/CallRuntime.cs`
- `Runtime/AsyncRuntime.cs`
- `Runtime/ValueOps.cs`
- `Runtime/CollectionOps.cs`
- `Runtime/ObjectRuntime.cs`
- `Runtime/ExceptionRuntime.cs`
- `Runtime/BindingRuntime.cs`
- `Runtime/ValuePrinter.cs`

Schritte:
- [x] VM als `partial class` oder ueber State + Services aufspalten.
- [x] `VmState` einziehen fuer:
  - stack
  - scopes
  - call stack
  - try handlers
  - function table
  - instruction list
- [x] `ExecutionEngine` fuer Loop und Instruktionsdispatch.
- [x] `CallRuntime` fuer:
  - closure invocation
  - argument binding
  - `CALL`
  - `CALL_INDIRECT`
  - return wrapping
  - hot-start child VM
- [x] `AsyncRuntime` fuer:
  - `AWAIT`
  - `YIELD`
  - awaitable adaptation
  - continuation resume
- [x] `ValueOps` fuer Numeric Ops, Truthiness, Comparisons.
- [x] `CollectionOps` fuer arrays, dicts, slices, delete, mutable locks.
- [x] `ObjectRuntime` fuer:
  - field access
  - static access
  - visibility
  - const rules
  - destroy/lifecycle
- [x] `BindingRuntime` fuer builtins, intrinsics und plugin binding.
- [x] `ValuePrinter` fuer JSON/print formatting.
- [x] `ExceptionRuntime` fuer try/catch/finally, throw und language stack.

DoD:
- [x] Der `switch` kann vorerst bleiben.
- [x] VM-Verantwortungen sind logisch getrennt.
- [x] Keine Performance-/Semantikregression im Async- oder Collections-Pfad.

Regressionstests:
- [x] `226_async_await_nonblocking_collections`
- [x] `230_async_hot_start_call_order`
- [x] `233_await_generic_task_valuetask`
- [x] `236_destroy_builtin`
- [x] `240_async_shared_state_serialized`
- [x] `243_async_shared_collection_aliases`
- [x] `101_core_expressions`
- [x] `102_control_flow_and_match`
- [x] `301_try_throw_finally`
- [x] `489_index_non_integer_fail`
- [x] `490_slice_non_integer_bound_fail`
- [x] `103_collections_and_delete`
- [x] `202_classes_enums_inheritance`
- [x] `203_nested_classes_and_object_init`
- [x] `220_member_access_and_initializer_valid`
- [x] `222_visibility_access_valid`
- [x] `223_constructor_alias_read_valid`
- [x] `420_const_assignment_fail`
- [x] `460_nested_constructor_missing_outer_fail`
- [x] `473_private_member_access_fail`
- [x] `487_delete_missing_index_var_fail`
- [x] `493_slice_set_string_immutable_fail`

## Phase 9 - Plugin-Binding vom VM-Kern entkoppeln

Ziel:
- Plugin-Lader und Runtime-Binding klar trennen.

Schritte:
- [x] Plugin-Lader bleibt fuer Assembly/Attribute/Registrierung zustaendig.
- [ ] VM bekommt nur noch abstrahierte Registries.
- [x] `LoadPlugin` und `LoadPluginsFrom` als Binding-Schicht kapseln.
- [x] Intrinsic-Bindung und Reserved-Name-Checks in `BindingRuntime` konzentrieren.
- [ ] Optional kleines Interface fuer VM-Binding einfuehren.

DoD:
- [x] Plugin-Mechanik ist nicht mehr mit Collection-/Object-/Dispatch-Code verschmolzen.

Regressionstests:
- [x] `231_plugin_nonblocking_loader`
- [x] `232_plugin_http_server_async`
- [x] `495_plugin_multifile_reload`
- [x] `506_plugin_register_failfast_fail`

## Phase 10 - Diagnostics, LSP und Doku nachziehen

Ziel:
- Nach dem Umbau keine unsichtbaren Brueche in Editor- und Diagnosepfaden behalten.

Schritte:
- [x] LSP-Analysepfad pruefen:
  - Parser-Aufruf
  - Compiler-Aufruf
  - Source overlays
  - symbols
  - semantic tokens
- [x] Fehlertexte gegen alte Edgecases vergleichen.
- [x] Doku aktualisieren, falls Architekturpfade oder Plugin-/Import-Semantik angepasst wurden.
- [x] Neue interne Architektursektion im Repo dokumentieren.

DoD:
- [x] LSP bleibt funktional.
- [x] Keine stillen Diagnoseverschlechterungen.

## Phase 11 - Cleanup und Endzustand

Ziel:
- Altcode, tote Helfer und Migrationsreste entfernen.

Schritte:
- [ ] Nicht mehr genutzte Parser-Hilfen entfernen.
- [ ] Nicht mehr genutzte Compiler-Zwischenpfade entfernen.
- [x] Nicht mehr genutzte VM-Hilfen entfernen.
- [ ] Ordnerstruktur konsolidieren.
- [x] Changelog in dieser Roadmap aktualisieren.

DoD:
- [x] Keine Parallelarchitekturen mehr.
- [x] Die neue Struktur ist die einzige produktive Struktur.

## Konkrete Reihenfolge fuer kleine PRs

Empfohlene PR-Reihenfolge:

- [x] PR1 Parser in partial-Dateien splitten
- [x] PR2 TokenCursor einfuehren
- [x] PR3 ParserContext einfuehren
- [x] PR4 Importauflosung aus Parser ziehen
- [x] PR5 Namespace-/Param-Lowering aus Parser ziehen
- [x] PR6 Compiler in Pipeline + Context schneiden
- [x] PR7 SymbolIndex + TopLevelValidator
- [x] PR8 ClassCatalog + InterfaceCatalog
- [x] PR9 Semantikregeln aus Bytecode-Phase loesen
- [x] PR10 BytecodeEmitter einfuehren
- [x] PR11 StmtEmitter + ExprEmitter split
- [x] PR11b PatternEmitter split
- [x] PR11c ClassEmitter split
- [x] PR11d FlowEmitter + CallEmitter split
- [x] PR12 VM in State + Engine split
- [x] PR13 AsyncRuntime + CallRuntime split
- [x] PR14 CollectionOps + ObjectRuntime split
- [x] PR15 BindingRuntime + Print/JSON split
- [x] PR15b ValueOps + ExceptionRuntime split
- [x] PR16 Cleanup + Doku + LSP-Abgleich

## Dinge, die wir bewusst spaeter anfassen

- [ ] Neues Opcode-Design
- [ ] Neue IR vor Abschluss der Compiler-Pass-Trennung
- [ ] JIT oder Performance-Spezialumbauten
- [ ] Grossflaechige AST-Neumodellierung
- [ ] Plugin-Sandboxing als separates Sicherheitsprojekt

## Red Flags waehrend der Umsetzung

Wenn einer dieser Punkte auftritt, Phase sofort stoppen und neu schneiden:

- [ ] Parser erzeugt weiterhin Runtime- oder Lowering-Nebenprodukte.
- [ ] Compiler validiert und emittiert denselben Sachverhalt in zwei Pfaden.
- [ ] VM-Serviceklassen greifen kreuz und quer direkt auf fremde Teilverantwortungen zu.
- [ ] Neue Architektur existiert parallel zum Altcode ohne klaren Umschaltpunkt.
- [ ] Edgecases werden "temporar" deaktiviert statt sauber angepasst.

## Teststrategie

Vor jedem PR:

- [ ] `dotnet build -v minimal CFGS_VM.sln`
- [ ] Selektive Edgecases fuer den Zielbereich

Nach jedem PR:

- [ ] `dotnet build -v minimal CFGS_VM.sln`
- [ ] `powershell -ExecutionPolicy Bypass -File .\\CFGS_NE\\_edgecases\\run_all_edgecases.ps1 -SkipBuild`

Optional fuer schnellere lokale Schleifen:

- [ ] Parser-nahe PRs:
  - `101`, `102`, `205`, `212`, `215`, `401`, `403`
- [ ] Compiler-nahe PRs:
  - `218`, `221`, `239`, `452`, `456`, `468`
- [ ] VM-nahe PRs:
  - `226`, `230`, `233`, `236`, `237`, `243`

## Changelog

- 2026-04-07: PR16 fuer Phase 10/11 umgesetzt.
  - `14_internal_architecture.md` eingefuehrt.
  - `01_getting_started_and_running.md` und `13_visual_studio_code_and_lsp.md` auf die produktive Architektur und den gemeinsamen Analysepfad nachgezogen.
  - Die letzten deaktivierten Legacy-Pfade aus `CFGS_NE/VMCore/VM.cs` entfernt; `VM.cs` enthaelt damit keine `#if false`-Parallelarchitektur mehr.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - LSP-Smokes ueber `CFGS_NE\\_edgecases\\_tmp_lsp_test.dll` und `CFGS_NE\\_edgecases\\_tmp_lsp_interface_smoke.dll`
    - Fehlertext-Smokes (`301`, `489`, `490`)
    - voller Edgecase-Run gruen

- 2026-04-07: Initiale Refactor-Roadmap fuer Parser, Compiler und VM angelegt.
- 2026-04-07: Phase 0 als Arbeitsbasis hergestellt und Phase 1 abgeschlossen.
  - `Parser` auf `partial class` umgestellt.
  - Split in:
    - `CFGS_NE/Analytic/Core/Parser.cs`
    - `CFGS_NE/Analytic/Core/Parser.Imports.cs`
    - `CFGS_NE/Analytic/Core/Parser.Statements.cs`
    - `CFGS_NE/Analytic/Core/Parser.Expressions.cs`
    - `CFGS_NE/Analytic/Core/Parser.FunctionParams.cs`
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Parser-Smokes (`101`, `205`, `215`, `499`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR2 fuer Phase 2 umgesetzt.
  - `CFGS_NE/Analytic/Core/TokenCursor.cs` eingefuehrt.
  - `Parser` intern auf Cursor-Eigenschaften fuer `Current` und `Next` umgestellt.
  - `Eat` in den `TokenCursor` verlagert.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Parser-Smokes (`101`, `205`, `215`, `499`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR3 fuer Phase 2 umgesetzt.
  - `CFGS_NE/Analytic/Core/ParserContext.cs` eingefuehrt.
  - Funktions-, Klassen-, Loop-, Out- und Rekursionstiefe aus rohen Parser-Feldern in `ParserContext` verlagert.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Parser-Smokes (`101`, `205`, `215`, `499`, `500`, `503`, `504`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR4 fuer Phase 3 umgesetzt.
  - `CFGS_NE/Analytic/Modules/SourceResolver.cs` eingefuehrt.
  - `CFGS_NE/Analytic/Modules/ImportResolver.cs` eingefuehrt.
  - Datei-, HTTP-, DLL-, Cache- und rekursive Importauflosung aus `Parser` in Resolver-Klassen verlagert.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Import-Regressionen (`207`, `209`, `210`, `401`, `402`, `403`, `413`, `414`, `415`, `416`, `425`, `427`, `428`, `494`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR5 fuer Phase 4 umgesetzt.
  - Raw-AST-Knoten fuer Namespace-Deklarationen, Import-Aliase und Funktionsparameter eingefuehrt.
  - `CFGS_NE/Analytic/Lowering/NamespaceLowerer.cs`, `ParamLowerer.cs` und `SyntaxLowerer.cs` eingefuehrt.
  - Parser erzeugt keine synthetischen Default-/Destructure-Initialisierer oder Namespace-/Import-Alias-Statements mehr direkt.
  - Lowering-Pipeline nach `Parse()` in Runtime, LSP und Import-Resolver eingehangen.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Lowering-/Namespace-Regressionen (`205`, `211`, `212`, `215`, `239`, `402`, `421`, `429`, `430`, `436`, `443`, `444`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR6 fuer Phase 5 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/CompilationContext.cs`, `CompilationPlan.cs` und `CompilationPipeline.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Compiler.Pipeline.cs` eingefuehrt.
  - `Compiler.Compile()` auf reine Pipeline-Orchestrierung reduziert.
  - Compiler-Zustand fuer Funktionen, Klassen, Interfaces und Bytecode in `CompilationContext` gezogen.
  - Symbolindex, Typegraph-Aufloesung, Semantikpruefungen und Emission als getrennte Pipeline-Schritte geschnitten.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Compiler-Regressionen (`218`, `224`, `225`, `239`, `452`, `456`, `483`, `485`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR7 fuer Phase 5 umgesetzt.
  - `CFGS_NE/Analytic/Semantics/SymbolIndex.cs` eingefuehrt.
  - `CFGS_NE/Analytic/Semantics/TopLevelValidator.cs` eingefuehrt.
  - Qualified Class-/Interface-Index, Namespace-Sicht und Funktionsvorabregistrierung aus `Compiler.Pipeline` in `SymbolIndex` verlagert.
  - Deklarationsnahe Top-Level-Pruefungen fuer Klassen und Interfaces in `TopLevelValidator` verlagert und als eigener Pipeline-Schritt eingehangen.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Compiler- und Symbolindex-Regressionen (`215`, `221`, `239`, `439`, `472`, `508`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR8 fuer Phase 5 umgesetzt.
  - `CFGS_NE/Analytic/Semantics/ClassCatalog.cs` eingefuehrt.
  - `CFGS_NE/Analytic/Semantics/InterfaceCatalog.cs` eingefuehrt.
  - Typegraph-Aufbau fuer Klassen und Interfaces aus `Compiler` in getrennte Katalogklassen verlagert.
  - Vererbungsnormalisierung, Toposort und Interface-Basisvalidierung laufen jetzt ueber die Catalogs.
  - `Compiler.ResolveTypeGraph()` delegiert nur noch an `ClassCatalog` und `InterfaceCatalog`.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Class-/Interface-Catalog-Regressionen (`215`, `218`, `221`, `239`, `409`, `456`, `468`, `508`, `509`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR9 fuer Phase 5 umgesetzt.
  - `CFGS_NE/Analytic/Semantics/ClassSemanticValidator.cs` eingefuehrt.
  - `CFGS_NE/Analytic/Semantics/MemberAccessRules.cs` eingefuehrt.
  - Override-, Base-Constructor- und Interface-Implementierungspruefungen aus `Compiler` in `ClassSemanticValidator` verlagert.
  - Memberaccess-, Visibility- und Initializer-Regeln aus `Compiler` in `MemberAccessRules` verlagert.
  - `Compiler.RunSemanticChecks()` und die verbliebenen Compile-Zugriffspfade delegieren jetzt in die Semantics-Schicht.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Semantik- und Memberaccess-Regressionen (`218`, `220`, `224`, `225`, `239`, `452`, `456`, `461`, `462`, `463`, `464`, `465`, `468`, `469`, `473`, `474`, `475`, `476`, `483`, `485`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR10 fuer Phase 6 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/BytecodeEmitter.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Codegen/BytecodeEmissionContext.cs` eingefuehrt.
  - Gemeinsamer Emissionszustand fuer Locals, Break-/Continue-Patches, Receiver-/Klassenkontext, Scope- und Async-Tiefe aus `Compiler` in `BytecodeEmissionContext` gezogen.
  - `Compiler.EmitProgram()` delegiert jetzt an `BytecodeEmitter`, waehrend die eigentlichen Emit-Helfer zunaechst noch im Compiler verbleiben.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Emissions-Regressionen (`102`, `203`, `204`, `220`, `226`, `237`, `238`, `243`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR11 fuer Phase 6 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/Emitters/StmtEmitter.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Codegen/Emitters/ExprEmitter.cs` eingefuehrt.
  - `CompileStmt` und `CompileExpr` als Partial-Dateien aus `Compiler.cs` herausgezogen, ohne die Emissionslogik funktional zu aendern.
  - `StmtEmitter` an den ausgelagerten `BytecodeEmissionContext` angepasst (`ReceiverContextKind`, `LoopLeavePatch`, `currentClass`-Lookup).
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Emissions-Regressionen (`102`, `203`, `204`, `220`, `226`, `237`, `238`, `243`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR11b fuer Phase 6 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/Emitters/PatternEmitter.cs` eingefuehrt.
  - Match-/Destructure-Helfer sowie `MatchStmt`-/`MatchExpr`-Emission aus `Compiler.cs`, `StmtEmitter.cs` und `ExprEmitter.cs` nach `PatternEmitter` gezogen.
  - `StmtEmitter` und `ExprEmitter` delegieren Match-Faelle jetzt an `CompileMatchStatement()` bzw. `CompileMatchExpression()`.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Match-/Destructure-Regressionen (`102`, `206`, `208`, `212`, `213`, `214`, `418`, `419`, `426`, `434`, `435`, `436`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR11c fuer Phase 6 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/Emitters/ClassEmitter.cs` eingefuehrt.
  - Klassen-, Member- und Initializer-Emission aus `StmtEmitter` und `ExprEmitter` nach `ClassEmitter` gezogen.
  - `StmtEmitter` delegiert `ClassDeclStmt` jetzt an `CompileClassDeclaration()`.
  - `ExprEmitter` delegiert `NewExpr` und `ObjectInitExpr` jetzt an `CompileNewExpression()` bzw. `CompileObjectInitializerExpression()`.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Klassen-/Initializer-Regressionen (`202`, `203`, `219`, `220`, `221`, `223`, `225`, `239`, `460`, `461`, `462`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR11d fuer Phase 6 umgesetzt.
  - `CFGS_NE/VMCore/Codegen/Emitters/FlowEmitter.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Codegen/Emitters/CallEmitter.cs` eingefuehrt.
  - Schleifen-, Block-/If-, `try`, `using`, `yield`, `break`/`continue`, `return`, `out` und bedingte Ausdrucksemission nach `FlowEmitter` gezogen.
  - `CallExpr`, `MethodCallExpr`, `NamedArgExpr` und `SpreadArgExpr` nach `CallEmitter` gezogen.
  - `EmitPushScope`, `EmitPopScope` und `ScopePopsTo` aus `Compiler.cs` in `FlowEmitter` verschoben.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Flow-/Call-Regressionen (`102`, `201`, `204`, `205`, `211`, `227`, `230`, `237`, `238`, `301`, `405`, `406`, `422`, `423`, `424`, `431`, `432`, `433`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR12 fuer Phase 8 umgesetzt.
  - `CFGS_NE/VMCore/VM.cs` auf `partial class` umgestellt und auf einen zentralen `VmState` verdrahtet.
  - `CFGS_NE/VMCore/Runtime/VmState.cs` eingefuehrt fuer Stack, Scopes, Call-Stack, Try-Handler, Funktionstabellen, Programmspeicher und Await-Resume-Zustand.
  - `CFGS_NE/VMCore/Runtime/ExecutionEngine.cs` eingefuehrt fuer `Run`, `RunAsync`, `RunUntilAwaitOrHalt`, Await-Resume und den Dispatch-Loop.
  - Der Opcode-`switch` in `HandleInstruction()` bleibt bewusst in `VM.cs`, damit `PR13` Runtime-Schnitt und Verhaltensumbau sauber trennt.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive VM-/Async-/Exception-Regressionen (`102`, `201`, `204`, `205`, `211`, `226`, `227`, `228`, `229`, `230`, `236`, `301`, `405`, `406`, `422`, `423`, `424`, `431`, `432`, `433`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR13 fuer Phase 8 umgesetzt.
  - `CFGS_NE/VMCore/Runtime/AsyncRuntime.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Runtime/CallRuntime.cs` eingefuehrt.
  - `InvokeClosureSync`, Hot-Start-Child-VM, Argumentbindung, Builtin-/Intrinsic-Call-Helfer, `CALL`, `CALL_INDIRECT` und `RET` aus `CFGS_NE/VMCore/VM.cs` in `CallRuntime` verschoben.
  - `AWAIT`, `YIELD`, Awaitable-Adaptation und `RunHotStartEntryAsync` aus `CFGS_NE/VMCore/VM.cs` in `AsyncRuntime` verschoben.
  - `HandleInstruction()` in `CFGS_NE/VMCore/VM.cs` delegiert die Opcode-Pfade jetzt nur noch an Runtime-Helfer.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Call-/Async-/Constructor-/Return-Regressionen (`102`, `201`, `202`, `203`, `204`, `205`, `211`, `219`, `226`, `227`, `228`, `229`, `230`, `236`, `239`, `240`, `241`, `242`, `243`, `301`, `405`, `406`, `422`, `423`, `424`, `431`, `432`, `433`, `460`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR14 fuer Phase 8 umgesetzt.
  - `CFGS_NE/VMCore/Runtime/CollectionOps.cs` eingefuehrt und vervollstaendigt.
  - `CFGS_NE/VMCore/Runtime/ObjectRuntime.cs` vervollstaendigt.
  - Mutable locks, Listen-/Dictionary-Helfer, Slice-Operationen, Delete-Pfade, `INDEX_GET`/`INDEX_SET`, Visibility-/Const-Zugriffe sowie Destroy-/Lifecycle-Logik aus `CFGS_NE/VMCore/VM.cs` in Runtime-Services verschoben.
  - `HandleInstruction()` in `CFGS_NE/VMCore/VM.cs` delegiert Collection-/Object-Opcode-Pfade jetzt an `CollectionOps` und `ObjectRuntime`.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Collections-/Object-/Visibility-Regressionen (`103`, `202`, `203`, `220`, `222`, `223`, `226`, `236`, `243`, `420`, `460`-`479`, `480`-`489`, `490`-`499`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR15 fuer Phase 8/9 umgesetzt.
  - `CFGS_NE/VMCore/Runtime/BindingRuntime.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Runtime/ValuePrinter.cs` eingefuehrt.
  - `Builtins`, `Intrinsics`, `LoadPlugin`, `LoadPluginsFrom`, Intrinsic-Bindung und Reserved-Name-Checks aus `CFGS_NE/VMCore/VM.cs` nach `BindingRuntime` verschoben.
  - `FormatVal`, `PrintValue`, `WriteJsonValue` und `JsonStringify` aus `CFGS_NE/VMCore/VM.cs` nach `ValuePrinter` verschoben.
  - `CollectionOps` und `ObjectRuntime` verwenden Reserved-Name-Helfer jetzt ueber `BindingRuntime`, statt lokale Doppeldefinitionen zu tragen.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Binding-/Print-/Plugin-Regressionen (`101`, `103`, `104`, `229`, `231`, `232`, `233`, `234`, `495`, `506`)
    - voller Edgecase-Run gruen
- 2026-04-07: PR15b fuer Phase 8 umgesetzt.
  - `CFGS_NE/VMCore/Runtime/ValueOps.cs` eingefuehrt.
  - `CFGS_NE/VMCore/Runtime/ExceptionRuntime.cs` eingefuehrt.
  - Numerische Operatoren, Truthiness, Vergleiche, `try`-/`throw`-Routing und Language-Stack-Helfer aus `CFGS_NE/VMCore/VM.cs` in Runtime-Services verschoben.
  - `HandleInstruction()` in `CFGS_NE/VMCore/VM.cs` delegiert Value- und Exception-Opcode-Pfade jetzt nur noch an `ValueOps` und `ExceptionRuntime`.
  - Verifikation:
    - `dotnet build -v minimal CFGS_VM.sln`
    - selektive Value-/Exception-Regressionen (`101`, `102`, `301`, `489`, `490`)
    - voller Edgecase-Run gruen
