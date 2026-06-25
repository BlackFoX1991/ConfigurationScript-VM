  # Vollständige statische Typisierung für CFGS

  ## Zusammenfassung

  - Ziel: CFGS wird statisch typsicher auf allen Sprachflächen, nicht nur auf Funktionen und Properties.
  - Rollout: direkt strikt. Es gibt keinen langen Übergangsmodus; der Merge-Branch muss Core, Tooling, Samples, Docs
    und offizielle Plugins gemeinsam anheben.

  - Festgelegte Leitentscheidungen:
      - offizielles Typvokabular bleibt Int, Long, Double, Decimal, BigInt, String, Boolean, Char, Byte, Float, Void,
        Any, Null

      - Nullability ist non-null by default
      - nullable Oberfläche ist T?
      - alle Deklarationen sind explizit typisiert
      - nur sichere Widenings sind implizit
      - Array<T>, Dictionary<K,V>, Task<T> kommen direkt in v1
      - Interfaces bleiben nominal + strukturell
      - Interop bleibt nur über explizite Any-Grenzen möglich

  ## Öffentliche Sprachänderungen

  - Pflichtsyntax für Typen:
      - var x: Int = 1;
      - const name: String = "A";
      - property Id: Int { get; set; }
      - func test(param: Int) -> Int { ... }
      - class User(id: Int) { ... }
      - interface IRepo { func get(id: Int) -> User?; }

  - Diese Pflicht gilt für:
      - lokale und globale var/const
      - Klassenfelder
      - Properties
      - Funktionsparameter
      - Funktions-Return-Typen
      - Klassenkopf-Parameter
      - Interface-Methoden, Interface-Properties und Indexer-Signaturen
      - Funktionsausdrücke und Closures

  - Generische Typoberfläche in v1:
      - nur Array<T>, Dictionary<K,V>, Task<T>
      - keine benutzerdefinierten generischen Klassen, Interfaces oder Funktionen in v1

  - Nullability:
      - T ist nicht-nullbar
      - T? ist der einzige öffentliche Nullable-Mechanismus
      - es gibt keine allgemeinen Union-Typen in v1

  - Konvertierungen:
      - implizit nur sichere Widenings, z. B. Int -> Long, Int -> Double
      - kein stilles Narrowing
      - explizite Primitive-Konvertierung läuft über bestehende Builtins wie int(), long(), double(), decimal(),
        char(), bool(), bigint()

      - zusätzlich kommt ein checked builtin cast(value, Type) für Klassen-, Interface- und Any-Grenzen

  - Typinferenz:
      - keine allgemeine Typinferenz für benannte Deklarationen
      - einzige erlaubte abgeleitete Bindungen sind Pattern-/Destructuring-Bindings aus einer bereits statisch
        typisierten Quelle

  ## Implementierungsänderungen

  - Frontend und Syntax
      - neuen TypeRef-Ast einführen: primitive Typen, nullable Typen, generische Built-in-Typen, Klassen-/Interface-
        Referenzen, Any, Void, Null

      - Parser für : Type, -> Type, T?, Array<T>, Dictionary<K,V>, Task<T> erweitern
      - Formatter und Syntax-Rewriter müssen die neuen Typknoten verlustfrei erhalten

  - Gebundene Typwelt
      - separates internes Typsystem einführen, nicht nur String-Namen
      - Typkategorien: Primitive, Nullable, Array, Dictionary, Task, Class, Interface, Function, Any, Null, Void
      - Klassische Klassen-/Interface-Auflösung bleibt erhalten und wird in diese Typwelt eingebunden

  - Statischer Typchecker
      - neue Compilerphase nach Type-Graph-Auflösung und vor Bytecode-Emission
      - prüft:
          - Deklarationen und Initializer
          - Assignments und Compound-Assignments
          - Funktionsaufrufe inkl. Named Args, Defaults und Restparametern
          - Return-Typen
          - Properties und Property-Initializer
          - Klassenfelder
          - Klassenkopf-Parameter und init
          - Overrides, Vererbung und Interface-Implementierung
          - Indexer
          - await, Task<T>, yield
          - match, Destructuring und Foreach-Bindings
          - Collection-Literale und Dictionary-Merges

      - strukturelle Interface-Kompatibilität wird compile-time mitgeprüft, nicht erst zur Laufzeit

  - Flow-Narrowing
      - v1 enthält gezieltes Flow-Narrowing nur für:
          - x is T
          - x == null
          - x != null
          - erfolgreiches cast(value, Type) Ergebnis

      - kein allgemeines Truthiness- oder Control-Flow-Inferenzsystem darüber hinaus

  - Runtime, Metadata, CFB
      - Funktions-, Property- und Interface-Metadaten werden um Typinformationen erweitert
      - CFB-Metadaten werden versioniert erweitert, damit kompilierte Artefakte Typinformationen mittragen
      - Laufzeit bleibt grundsätzlich dynamisch ausführend, aber Any- und Cast-Grenzen erhalten gezielte Runtime-
        Prüfungen

  - Interop und Plugins
      - Builtins, Intrinsics, Plugin-Exports und Native-Interop müssen deklarierte Typsignaturen tragen
      - legacy oder unvollständig beschriebene Interop-Flächen werden explizit als Any modelliert
      - Any darf nicht still in konkrete Typen fließen; dafür sind cast() oder explizite Primitive-Konvertierungen
        nötig

  - Tooling
      - LSP, Hover, Signature Help, Symbols und Formatter spiegeln alle neuen Typinformationen
      - Typdiagnostik bekommt einen eigenen stabilen Codebereich, z. B. CFGS8200–CFGS8299
      - Samples, Language Spec und Standard-Library-Doku werden parallel mitgezogen

  ## Lieferphasen

  1. Phase 0: RFC und Invarianten
      - Sprachregeln, Typmatrix, Konvertierungsregeln, Any-Grenzen, Nullability und Collection-Typen schriftlich
        fixieren

  2. Phase 1: Syntax- und AST-Plumbing
      - Parser, AST, Formatter, LSP-Syntax, CFB-Metadaten, ohne vollständige semantische Strenge

  3. Phase 2: Gebundene Typwelt
      - TypeRef-Binding, Symboltypen, Builtin-/Plugin-Signaturmodell, Standard-Library-Signaturen

  4. Phase 3: Kern-Checker
      - Deklarationen, Assignments, Calls, Returns, Felder, Properties, Klassen, Interfaces, Overrides

  5. Phase 4: Erweiterte Sprachflächen
      - Collections, Async, Task<T>, Destructuring, match, Flow-Narrowing, Any-Grenzen, cast

  6. Phase 5: Repo-Migration
      - offizielle Plugins, Samples, Edgecases, Docs, Tooling und Tests vollständig auf strict typing anheben

  ## Testplan

  - Parser-/Formatter-Roundtrips für jede neue Typed-Syntax
  - Erfolgsfälle für:
      - typed var/const
      - typed Felder und Properties
      - typed Funktionen und Closures
      - typed Klassenkopf-Parameter
      - typed Interfaces und Indexer
      - Array<T>, Dictionary<K,V>, Task<T>
      - nullable Returns und nullable Properties

  - Fehlerfälle für:
      - fehlende Typannotation
      - falschen Return-Typ
      - inkompatible Assignment-Typen
      - unsichere Narrowings
      - falsche Collection-Elementtypen
      - ungültige Nullable-Zuweisungen
      - strukturell inkompatible Interface-Zuweisungen
      - Any nach konkret ohne cast

  - Flow-Fälle für:
      - if (x is Foo)
      - if (x != null)
      - var y: Foo = cast(x, Foo);

  - Regressionen:
      - LSP-Tests
      - typed Versionen der offiziellen Samples
      - CFB-Laden/Speichern mit Typmetadaten

  ## Annahmen und Defaults
    Schicht kommen.

  - Bereits vorhandene Primitive-Konvertierungsbuiltins bleiben die einzige Standardlösung für explizite Numeric-
    Konvertierung.

  - Der Merge erfolgt erst, wenn das Repo selbst vollständig strict-typed gebaut, geprüft und dokumentiert ist.
  - Ziel ist keine „gradually typed“ Sprache, sondern ein konsistenter, strikter Sprachmodus ohne stilles dynamisches
    Fallback.