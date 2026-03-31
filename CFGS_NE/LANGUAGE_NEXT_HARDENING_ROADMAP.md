# CFGS Language Next Hardening Roadmap

Stand: 2026-03-08

## Ziel

Die naechsten offenen Luecken aus der Tiefenanalyse strukturiert schliessen:
- erst Semantikluecken bei `async/await`,
- danach Runtime-Robustheit,
- dann Diagnose-/Doku-Konsistenz.

## Reihenfolge (Schritt fuer Schritt)

- [x] 1) `await`-Kontext an `async` binden (C#-nahe Regel)
  - Problem:
    - `await` war in normalen Funktionen erlaubt.
  - Aufgabe:
    - Parser-Kontext fuer async-Funktionen einfuehren.
    - `await` in non-async Funktionen als Parserfehler behandeln.
    - Betroffene Edgecases/Samples auf `async func` anpassen.
  - DoD:
    - Neuer Negativfall `await` in non-async Funktion failt.
    - Voller Edgecase-Run bleibt gruen.
  - Umsetzung:
    - Parser: `_asyncFuncDepth` eingefuehrt und bei `async func` verwaltet.
    - Parser: Fehlertext `"await can only be used in async function statements"`.
    - Neuer Edgecase: `499_await_without_async_fail`.
    - Angepasst:
      - `_edgecases/204_out_and_await.cfs`
      - `_edgecases/226_async_await_nonblocking_collections.cfs`
      - `Samples/general_Tests/await.cfs`
      - `Samples/general_Tests/feature_07_visibility_foreach_out_intrinsics.cfs`
      - `Samples/general_Tests/feature_08_async_await_yield.cfs`

- [x] 2) `async`-Returnvertrag im VM-Kern implementieren
  - Problem:
    - `async` war nur markiert (kein Task-Return-Vertrag im VM-Callpfad).
  - Aufgabe:
    - VM-Callpfad fuer async-Funktionen auf Task-Rueckgabe umstellen.
    - Edgecases/Samples auf `await asyncCall` ausrichten.
  - DoD:
    - `async func` liefert reproduzierbar awaitbare Task-Resultate.
    - Edgecases fuer `typeof(asyncCall)` und `await asyncCall` sind gruen.
  - Umsetzung:
    - `Closure` traegt jetzt `IsAsync`.
    - `CallFrame` traegt `IsAsync`.
    - Rueckgaben aus async-Frames werden in `Task.FromResult(...)` gewrappt.
    - Neuer Edgecase: `228_async_function_task_contract`.
    - Voller Edgecase-Run weiterhin gruen.

- [x] 3) AwaitableAdapter fuer non-generic `Task`/`ValueTask` haerten
  - Problem:
    - Aktuelle `ContinueWith(... => null)`-Pfade koennen Fehler/Cancellation verschlucken.
  - Aufgabe:
    - Fault/Cancel propagieren statt still zu normalisieren.
  - DoD:
    - Non-generic Task-Fault landet als `AwaitError` im Sprachfehlerpfad.
    - Neue Edgecases (pluginnah) vorhanden.
  - Umsetzung:
    - `AwaitableAdapter` nutzt fuer non-generic `Task`/`ValueTask` jetzt direkte `await`-Wrapper statt `ContinueWith(... => null)`.
    - Neuer Edgecase `229_await_non_generic_task_valuetask` deckt Delay/Fault/Canceled fuer non-generic `Task` und `ValueTask` ab.
    - Dediziertes Edge-Plugin `CFGS.EdgeAwaitables.dll` liefert reproduzierbare non-generic Awaitables fuer die Suite.

- [x] 4) `yield`-Semantik finalisieren
  - Problem:
    - `yield` ist aktuell ein Scheduling-Statement ohne Generatorwerte.
  - Aufgabe:
    - Entscheidung treffen:
      - A) beim Scheduling-`yield` bleiben (klar dokumentieren), oder
      - B) Generator-Semantik (`yield value`) einfuehren.
  - DoD:
    - Gewaehlte Semantik ist im Parser/Compiler/VM und in Tutorials konsistent.
  - Umsetzung:
    - Entscheidung A umgesetzt: `yield` bleibt Scheduling-Statement ohne Rueckgabewert.
    - `yield` ist jetzt auf `async func` begrenzt (Parser + Compiler-Schutz).
    - Neuer Negativfall: `500_yield_without_async_fail`.
    - Edgecase `227_yield_statement` auf async Task-Vertrag umgestellt.
    - Compiler korrigiert Async-Flag-Weitergabe fuer Klassenmethoden (`FuncExpr` aus `FuncDeclStmt` uebernimmt jetzt `IsAsync`).

- [x] 5) Diagnostik-Positionskonsistenz verbessern
  - Problem:
    - Einzelne AST-Knoten (u.a. `await`/`func expr`) nutzen spaete Token-Positionen.
  - Aufgabe:
    - Starttoken-basierte Quellpositionen vereinheitlichen.
  - DoD:
    - Fehlerorte zeigen stabil auf das verursachende Token.
  - Umsetzung:
    - Lexer liefert Token-Positionen jetzt konsistent auf Token-Start (`_tokLine`/`_tokCol`), nicht auf Endposition.
    - Parser nutzt fuer zentrale AST-Knoten Starttoken-Positionen (u.a. `func expr`, `return`, Aufruf-/Index-/Slice-/Unary-/Binary-Expr sowie Kern-Control-Statements).
    - Neue Positions-Edgecases:
      - `503_await_position_fail` (`await`-Fehler zeigt korrekt auf `await`)
      - `504_async_func_expr_position_fail` (`expected 'func' after 'async'` zeigt korrekt auf `async`)

- [x] 6) Tutorial/Samples auf neuen Kernstand ziehen
  - Problem:
    - `async/await/yield` und neue Feature-Samples sind im Tutorial noch unterrepraesentiert.
  - Aufgabe:
    - Tutorial erweitern (Feature-Overview + Beispielaufrufe).
  - DoD:
    - Doku deckt `feature_06` bis `feature_08` sichtbar ab.
  - Umsetzung:
    - Tutorial um sichtbare `feature_06`-`feature_08` Sektion erweitert (Inhalt + Run-Kommandos).
    - `feature_06_plugins_integration.cfs` auf `async main` mit explizitem Async-Task-Contract-Check erweitert.
    - `feature_08_async_await_yield.cfs` um expliziten `Task<Object>`-Rueckgabetest fuer `async func` ergaenzt.

- [x] 7) Hot-Start Async-Aufrufe (optional C#-naeher)
  - Problem:
    - Async-Calls liefern jetzt Tasks, laufen aber aktuell weiter im bestehenden (eager) VM-Ablauf.
  - Aufgabe:
    - Optionalen Scheduler-/Execution-Ansatz fuer echte nicht-blockierende Async-Starts definieren.
  - DoD:
    - Klar dokumentierte Semantik + Edgecases fuer Aufrufreihenfolge.
  - Umsetzung:
    - VM startet `async`-Closures jetzt als Hot-Start in einer Child-VM und gibt sofort das gestartete `Task<Object>` zurueck.
    - Child-VM uebernimmt Funktions-/Builtin-/Intrinsic-Registries des aktuellen VM-Kontexts fuer konsistente Laufzeitauflosung.
    - Neuer Edgecase `230_async_hot_start_call_order` validiert Aufrufreihenfolge (sync Prefix vor Rueckgabe, Fortsetzung nach `await`).
    - `feature_08_async_await_yield.cfs` und Tutorial um Hot-Start-Semantik ergaenzt.

- [x] 8) Plugin-Invoke NonBlocking Handling + Parallelitaets-Haertung
  - Problem:
    - Plugin-Aufrufe liefen immer inline auf dem VM-Thread; blockierende Plugin-Methoden konnten den Interpreter ausbremsen.
    - Bei Hot-Start + parallelen Tasks gab es ungeschuetzte direkte `Env.Vars`-Zugriffe.
  - Aufgabe:
    - NonBlocking-Option fuer Builtin/Intrinsic-Aufrufe im Plugin-Vertrag einbauen.
    - PluginLoader fuer Attribut-Registrierung um NonBlocking/SmartAwait-Metadaten und sauberes Exception-Unwrap erweitern.
    - Kernzugriff auf Umgebungsvariablen fuer parallele Ausfuehrung robust machen.
  - DoD:
    - NonBlocking-Pluginfunktionen liefern sofort `Task<Object>` zurueck und blockieren den Caller nicht.
    - Voller Edgecase-Run bleibt gruen.
  - Umsetzung:
    - `BuiltinDescriptor`/`IntrinsicDescriptor` um `NonBlocking` erweitert (inkl. ABI-kompatibler Konstruktor-Ueberladungen fuer bestehende Plugins).
    - `BuiltinAttribute`/`IntrinsicAttribute` um `SmartAwait` + `NonBlocking` erweitert.
    - VM-Callpfad ruft NonBlocking-Builtins/Intrinsics im Background aus und gibt Task-basiert zurueck.
    - PluginLoader registriert Attribut-Methoden mit Metadaten (`SmartAwait`/`NonBlocking`) und entpackt `TargetInvocationException` auf Inner-Exception.
    - `Env` erhielt `SyncRoot`; VM nutzt gelockte Helper fuer lokale Variablenzugriffe.
    - Neuer Edgecase `231_plugin_nonblocking_loader` (manuelle + attributbasierte Builtins/Intrinsics, NonBlocking-Aufrufreihenfolge).

- [x] 9) Externe Plugin-Async-API haerten (`CFGS.Web.Http`, `CFGS.Microsoft.SQL`)
  - Problem:
    - Externe Plugins nutzten teils `Task.Run`-Wrapper oder Sync/Async-Mischpfade; serverseitige HTTP-Operationen hatten keine expliziten Async-Intrinsics.
  - Aufgabe:
    - HTTP-Builtins auf echte async I/O-Methoden umstellen (ohne unnoetiges `Task.Run`).
    - Async-Server-Intrinsics fuer `ServerHandle` bereitstellen.
    - SQL-Verbindung auf `OpenAsync` umstellen und Sync/Async-Ausfuehrung in Query-Dualpfaden sauber trennen.
    - Samples/Edgecases auf den neuen Stand bringen.
  - DoD:
    - `CFGS.Web.Http` und `CFGS.Microsoft.SQL` bauen erfolgreich in `dist`.
    - Plugin-Integrationssample und neue Edgecases laufen gruen.
  - Umsetzung:
    - `CFGS.Web.Http`:
      - `http_get`/`http_post`/`http_download` nutzen jetzt direkte async Helper (`HttpGetAsync`/`HttpPostAsync`/`HttpDownloadAsync`).
      - Neue Async-Intrinsics auf `ServerHandle`:
        - `start_async`, `stop_async`, `poll_async`, `respond_async`, `close_async`.
    - `CFGS.Microsoft.SQL`:
      - `sql_connect` nutzt `OpenAsync` via `ConnectAsync`.
      - `RegisterSqlDual(...)` fuehrt Sync- und Async-Bodies getrennt aus (kein `GetAwaiter().GetResult()` mehr im Sync-Pfad).
    - Tests/Doku:
      - `Samples/general_Tests/feature_06_plugins_integration.cfs` erweitert um Async-Server-API-Checks.
      - Neuer Edgecase `232_plugin_http_server_async`.
      - `_edgecases/run_all_edgecases.ps1` um `232` erweitert.
      - Tutorial ergaenzt um neue HTTP-Async-Intrinsics.

## Teststrategie pro Punkt

- Nach jedem Punkt:
  - selektiver Run der betroffenen Edgecases,
  - danach kompletter Lauf:
    - `powershell -ExecutionPolicy Bypass -File _edgecases\\run_all_edgecases.ps1 -SkipBuild`

## Changelog

- 2026-03-08: Roadmap angelegt.
- 2026-03-08: Punkt 1 abgeschlossen (Async-Kontext fuer `await` + Edgecase 499 + Suite gruen).
- 2026-03-08: Punkt 2 abgeschlossen (`async`-Returnvertrag als Task + Edgecase 228 + Suite gruen).
- 2026-03-09: Punkt 3 abgeschlossen (non-generic Awaitables propagieren Fehler/Cancellation + Edgecase 229 + Suite gruen).
- 2026-03-09: Punkt 4 abgeschlossen (Scheduling-`yield` finalisiert, auf async begrenzt + Edgecase 500 + Suite gruen).
- 2026-03-09: Punkt 5 abgeschlossen (Starttoken-Positionen in Lexer/Parser + Edgecases 503/504 + Suite gruen).
- 2026-03-09: Punkt 6 abgeschlossen (Tutorial deckt feature_06-08 sichtbar ab + Samples auf Async-Kernstand aktualisiert).
- 2026-03-09: Punkt 7 abgeschlossen (Hot-Start fuer async Calls + Edgecase 230 + Doku/Sample-Erweiterung).
- 2026-03-09: Punkt 8 abgeschlossen (Plugin NonBlocking Handling + Env-Parallelitaets-Haertung + Edgecase 231 + Suite gruen).
- 2026-03-09: Punkt 9 abgeschlossen (HTTP/SQL Plugin-Async-Haertung + Edgecase 232 + Suite gruen).
- 2026-03-31: Async-VM auf C#-naehere Hot-Start-Semantik nachgezogen.
  - Nested async CFGS calls starten jetzt ebenfalls eager innerhalb laufender async CFGS-Fortsetzungen.
  - Continuations laufen nicht mehr ueber einen globalen Root-Gate-Serialisierungspfad, sondern koennen wie in C# parallel weiterlaufen.
  - Arrays, Dictionaries, Instanzfelder und Static-Felder wurden dafuer im VM-Kern auf objektlokale Synchronisation umgestellt, damit parallele Mutationen die Runtime nicht strukturell zerlegen.
  - Read-modify-write-Logik bleibt bewusst nicht atomar; verlorene Updates auf gemeinsamem State bleiben moeglich wie in normalem C#-Code.
  - `feature_08_async_await_yield.cfs` sowie die Edgecases `230`, `240`, `243` und `244` decken die neue Semantik ab.
