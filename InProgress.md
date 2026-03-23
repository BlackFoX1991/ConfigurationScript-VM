# CFGS Code-Analyse - Gesamtbericht

Stand: 2026-03-23

## 1. FEHLENDE SPRACH-FEATURES (Hohe Priorität)

### 1.1 Standard Library - Fehlende Kern-Funktionen

**Array-Methoden** - nur `len`, `push`, `pop`, `insert_at`, `remove_range`, `replace_range`, `slice` vorhanden. Es fehlen:

| Methode | Zweck | Priorität |
|---------|-------|-----------|
| `sort(comparator?)` | Sortieren | Kritisch |
| `reverse()` | Umkehren | Kritisch |
| `map(fn)` | Transformation | Kritisch |
| `filter(fn)` | Filtern | Kritisch |
| `reduce(fn, init)` | Aggregation | Kritisch |
| `find(fn)` | Erstes Match | Hoch |
| `findIndex(fn)` | Index des ersten Match | Hoch |
| `indexOf(value)` | Element suchen | Hoch |
| `includes(value)` | Enthält-Prüfung | Hoch |
| `join(separator)` | Array zu String | Hoch |
| `every(fn)` | Alle erfüllen Bedingung | Mittel |
| `some(fn)` | Mindestens eins erfüllt | Mittel |
| `flat(depth?)` | Verschachtelte Arrays glätten | Mittel |

**String-Methoden** - es fehlen:

| Methode | Zweck | Priorität |
|---------|-------|-----------|
| `split(separator)` | String aufteilen | Kritisch |
| `startsWith(prefix)` | Prefix-Check | Hoch |
| `endsWith(suffix)` | Suffix-Check | Hoch |
| `indexOf(needle)` | Position finden | Hoch |
| `lastIndexOf(needle)` | Letzte Position | Mittel |
| `repeat(n)` | Wiederholen | Mittel |
| `padStart(len, char)` | Links auffüllen | Niedrig |
| `padEnd(len, char)` | Rechts auffüllen | Niedrig |

**Math-Funktionen** - nur `abs()` und `rand()` vorhanden. Es fehlen:

| Methode | Priorität |
|---------|-----------|
| `floor()`, `ceil()`, `round()` | Kritisch |
| `min()`, `max()` | Kritisch |
| `sqrt()` | Hoch |
| `pow()` (als Builtin, `**` existiert) | Mittel |
| `log()`, `log10()` | Mittel |
| `sin()`, `cos()`, `tan()` | Mittel |
| `PI`, `E` Konstanten | Mittel |
| `sign()`, `trunc()` | Niedrig |

**Regex** - komplett fehlend. Kein Pattern-Matching auf Strings möglich.

**Dictionary** - es fehlen: `entries()`, `merge()`, `clone()`

### 1.2 HTTP-Plugin - Fehlende HTTP-Methoden

Nur `GET` und `POST` vorhanden. Es fehlen:
- `http_put()`
- `http_delete()`
- `http_patch()`
- `http_head()`

Außerdem: `http_post` setzt Content-Type immer auf `text/plain` statt den Body-Typ zu erkennen oder konfigurierbar zu machen (`CFGS.Web.Http/CFGS_WEB_HTTP.cs:149`).

---

## 2. VM & COMPILER - Robustheit

### 2.1 Race Condition in Environment-Zugriff (Hoch)
**`CFGS_NE/VMCore/Extensions/Core/Env.cs:43-56`**

`TryGetValue` lockt nur den aktuellen Scope, liest aber den Parent-Scope ohne Lock:
```csharp
lock (SyncRoot) { /* current scope */ }
if (Parent != null) return Parent.TryGetValue(name, out value); // kein Lock!
```
Bei Hot-Start Async + parallelen Tasks kann das zu Race Conditions führen.

### 2.2 O(n) Lookup im Hot Path (Mittel)
**`CFGS_NE/VMCore/VM.cs:2682`**

```csharp
FunctionInfo? funcInfo = Functions.Values.FirstOrDefault(f => f.Address == funcAddr);
```
Lineare Suche durch alle Funktionen bei jedem `PUSH_CLOSURE`. Ein Reverse-Lookup `Dictionary<int, FunctionInfo>` (Address → FuncInfo) würde das auf O(1) bringen.

### 2.3 Fehlende Bounds-Checks auf Compiler-Stacks (Mittel)
**`CFGS_NE/VMCore/Compiler.cs`** - Mehrere `.Peek()` und `.Pop()` Aufrufe auf `_breakLists`, `_continueLists`, `_localVarsStack` ohne vorherige Count-Prüfung (Lines 2533, 2549, 2555, 2588, etc.). Ein Compiler-Bug könnte hier zu kryptischen `InvalidOperationException`s führen statt zu klaren Fehlermeldungen.

### 2.4 Generische Exceptions statt VMException (Niedrig)
An mehreren Stellen werden `Exception` gefangen und umverpackt statt spezifische Typen zu behandeln:
- `Compiler.cs:275, 314, 336, 351`
- `VM.cs:534`
- `PluginLoader.cs:241, 298, 345, 397`

### 2.5 VMException ohne InnerException (Niedrig)
**`CFGS_NE/VMCore/Extensions/VMException.cs:56`** - Der Konstruktor nimmt keinen `InnerException`-Parameter, wodurch Exception-Chains verloren gehen.

---

## 3. PARSER & LEXER

### 3.1 Keine Error Recovery (Hoch)
Der Parser bricht beim ersten Syntaxfehler komplett ab. Es gibt keinen Mechanismus um Tokens zu überspringen und weiterzuparsen. Das betrifft auch den LSP - ein einziger Tippfehler verhindert jegliche Analyse des restlichen Dokuments.

Der LSP kompensiert das teilweise durch Fallback auf das letzte erfolgreiche Modell - das ist ein guter Workaround, aber echte Recovery wäre besser.

### 3.2 Keine Rekursionstiefe-Limits (Mittel)
Kein Schutz gegen Stack Overflow bei:
- Tief verschachtelten Klassen (`class A { class B { class C { ... } } }`)
- Tief verschachtelten Destructuring-Patterns
- Tief verschachtelten Match-Patterns

### 3.3 Stille Fehler bei Zahlenliteralen (Niedrig)
**`Lexer.cs:376-429`** - Hex/Binary/Octal-Parsing hat try-catch Blöcke, die Parsing-Fehler verschlucken, ohne dem User eine sinnvolle Meldung zu geben.

---

## 4. SICHERHEIT

### 4.1 Crypto: PBKDF2 Minimum Iterations zu niedrig (Hoch)
**`CFGS.Security.Crypto/CFGS_SECURITY_CRYPTO.cs:326`**

```csharp
int iterations = ReadInt(args[2], "iterations", instr, minValue: 1);
```
`minValue: 1` ist gefährlich. NIST empfiehlt mindestens 100.000. Ein Minimum von z.B. 10.000 wäre sinnvoll, um Nutzer vor versehentlich schwachen Ableitungen zu schützen.

### 4.2 HTTP-Server: Keine Security-Header (Mittel)
**`CFGS.Web.Http/CFGS_WEB_HTTP.cs:545-573`** - `respond`/`respond_async` setzen keine Security-Header (`X-Content-Type-Options`, `X-Frame-Options`, etc.). Wenigstens `X-Content-Type-Options: nosniff` wäre sinnvoll als Default.

### 4.3 HTTP-Server: Keine Request-Size-Limits (Mittel)
Keine Validierung von Body-Größe, Header-Größe oder Pfad-Länge - potentiell anfällig für Resource Exhaustion.

### 4.4 SQL: Fehler-Messages leaken DB-Details (Niedrig)
**`CFGS.Microsoft.SQL/CFGS_MS_SQL.cs:907`** - Raw `SqlException.Message` wird durchgereicht, was Tabellennamen, Constraints etc. exponiert.

---

## 5. RESOURCE MANAGEMENT

### 5.1 Keine IDisposable auf Crypto-Handles (Mittel)
`RsaHandle`, `EcdsaHandle`, `Ed25519Handle`, `X25519Handle`, `X509CertHandle` haben `Dispose()`-Methoden, aber Nutzer müssen manuell `.close()` aufrufen. Ohne das leaken die Keys.

### 5.2 FileHandle: Flush-Fehler werden verschluckt (Niedrig)
**`FileHandle.cs:30`** - `try { _writer?.Flush(); } catch { }` im Dispose kann dazu führen, dass Daten stillschweigend verloren gehen.

---

## 6. LSP & TOOLING

### 6.1 Fehlende LSP-Features

| Feature | Nutzen | Aufwand |
|---------|--------|---------|
| `foldingRangeProvider` | Code-Folding in VSCode | Niedrig |
| `documentFormattingProvider` | Auto-Format | Mittel |
| `workspaceSymbolProvider` | Workspace-weite Symbolsuche | Mittel |
| `callHierarchyProvider` | Call-Hierarchie | Hoch |
| `inlayHintProvider` | Inline-Typ-Hints | Mittel |
| `codeLensProvider` | Reference-Counts über Code | Mittel |
| `documentLinkProvider` | Klickbare Import-Pfade | Niedrig |

### 6.2 LSP: Unsichere Property-Zugriffe (Mittel)
**`CFGS.Lsp/LspServer.cs:137, 147, 165, 172, 214, 272`** - Mehrere `.GetProperty()` Aufrufe ohne vorheriges `TryGetProperty`, was bei fehlerhaften Client-Nachrichten crashen kann.

### 6.3 LSP: Keine Timeout-Behandlung (Niedrig)
Kein Timeout auf Requests, kein Content-Length-Limit auf eingehende Nachrichten.

---

## 7. TEST-ABDECKUNG

### 7.1 Lücken in der Edgecase-Suite

Die Suite hat ca. 145 Tests in diesen Bereichen:
- `100-104`: Kern-Sprache (4 Tests)
- `200-235`: Advanced Features (35 Tests)
- `301`: Exception Handling (1 Test)
- `400-506`: Negative/Error Cases (100+ Tests)
- `600-705`: CLI/Build (10 Tests)

**Fehlende Test-Kategorien:**
- Kein Stress-Test (große Dateien, tiefe Rekursion, viele Imports)
- Kein Concurrency-Test (parallele async Tasks, Race Conditions)
- Kein Performance-Benchmark
- Keine Tests für Standard-Library-Builtins (`tojson`, `fromjson`, `fopen`, etc. als eigene Edge-Cases)
- Lücke zwischen 105-199 (keine Tests für diesen Nummernbereich)

---

## 8. ZUSAMMENFASSUNG & PRIORISIERUNG

### Sofort angehen (Kritisch)
- [ ] 1. **`split()` für Strings** - absolut fundamentale Operation, fehlt komplett
- [ ] 2. **`sort()`, `reverse()` für Arrays** - ohne das kann man Collections kaum nutzen
- [ ] 3. **`map()`, `filter()`, `reduce()` für Arrays** - funktionale Kernoperationen
- [ ] 4. **`floor()`, `ceil()`, `round()`, `min()`, `max()`** - grundlegende Math-Funktionen

### Bald angehen (Hoch)
- [ ] 5. **Env Race Condition** fixen (Parent-Lock)
- [ ] 6. **`indexOf()`, `find()`, `includes()` für Arrays**
- [ ] 7. **`startsWith()`, `endsWith()`, `indexOf()` für Strings**
- [ ] 8. **`join()` für Arrays**
- [ ] 9. **HTTP PUT/DELETE/PATCH**
- [ ] 10. **PBKDF2 Minimum-Iterations erhöhen**

### Mittelfristig (Mittel)
- [ ] 11. **Regex-Support** - eigenes Plugin oder StdLib-Erweiterung
- [ ] 12. **Parser Error Recovery** - mehrere Fehler auf einmal melden
- [ ] 13. **O(1) Function-Lookup** im VM Hot Path
- [ ] 14. **LSP: Folding, Formatting, Workspace Symbols**
- [ ] 15. **Rekursionstiefe-Limits** im Parser

### Irgendwann (Niedrig)
- [ ] 16. Math-Trigonometrie, PI/E Konstanten
- [ ] 17. `padStart`/`padEnd` für Strings
- [ ] 18. Plugin-Versionierung
- [ ] 19. `IDisposable` auf Crypto-Handles
- [ ] 20. LSP: Inlay Hints, Call Hierarchy, Code Lens
