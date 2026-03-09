# Einstieg und Ausfuehrung

## Was dieses Projekt ist

ConfigurationScript, kurz CFGS, ist in diesem Repository eine Bytecode VM mit eigenem Lexer, Parser, Compiler und Runtime. Skripte werden also nicht direkt aus dem AST interpretiert, sondern zuerst geparst und kompiliert und dann in der VM ausgefuehrt.

Fuer die Praxis bedeutet das zwei Dinge. Erstens ist die Sprache klar formalisiert. Zweitens lohnt es sich, die Dokumentation direkt am aktuellen Code auszurichten. Genau das macht dieser Ordner.

## Wichtige Projektordner

- `CFGS_NE` enthaelt Sprache, Parser, Compiler, Runtime und die CLI.
- `CFGS.StandardLibrary` liefert die Standard Builtins und Intrinsics.
- `CFGS.Web.Http` liefert HTTP Builtins und den eingebauten HTTP Server Handle.
- `CFGS.Microsoft.SQL` liefert die SQL Anbindung.
- `Documentation` enthaelt die neue Dokumentation.
- `CFGS_NE/Samples` und `CFGS_NE/_edgecases` sind gute Referenzen fuer reale Sprachverwendung.

## Bauen

Das gesamte Repository laesst sich ueber die Solution bauen.

```powershell
dotnet build CFGS_VM.sln
```

Die Plugins und die Standardbibliothek werden dabei als normale .NET Assemblies gebaut. In den Beispielskripten werden die DLLs typischerweise aus `dist/Debug/net10.0` importiert.

## Skript ausfuehren

Ein CFGS Skript ist in der Regel eine Datei mit der Endung `.cfs`.

```powershell
dotnet run --project CFGS_NE -- .\mein_script.cfs
```

Wenn du Skriptparameter brauchst, fuehrst du sie nach dem Marker `-p` oder `-params` an.

```powershell
dotnet run --project CFGS_NE -- .\mein_script.cfs -p alpha beta gamma
```

Im Skript kannst du sie dann ueber `cmdArgs()` lesen.

## REPL starten

Wenn du keine Datei uebergibst, startet die CLI in den interaktiven Modus.

```powershell
dotnet run --project CFGS_NE
```

Im REPL gibt es diese Kommandos.

- `help` zeigt die Hilfeseite.
- `exit` oder `quit` beendet die Sitzung.
- `clear` oder `cls` leert die Konsole.
- `debug` schaltet den Debug Modus um.
- `ansi` schaltet ANSI Farben um.
- `buffer:len` setzt die Groesse des Debug Puffers.

In der REPL Hilfe sind ausserdem diese Interaktionen hinterlegt.

- `Ctrl+Enter` fuehrt den aktuellen Mehrzeilenbuffer aus.
- `Ctrl+Backspace` leert den aktuellen Buffer.
- `$L <Zeile> <Inhalt>` editiert eine bestimmte Zeile.
- Pfeiltasten hoch und runter koennen beim Editieren helfen.

## Wichtige CLI Optionen

- `-d` oder `-debug` aktiviert den Debug Modus vor dem Start.
- `-s buffer <zahl>` setzt die Groesse des Debug Puffers.
- `-s ansi 0` oder `-s ansi 1` schaltet ANSI an oder aus.
- `-p` oder `-params` trennt Skriptargumente von CLI Argumenten.

Im Debug Modus wird nach der Ausfuehrung eine `log_file.log` im aktuellen Arbeitsverzeichnis geschrieben.

## Arbeitsverzeichnis waehrend der Ausfuehrung

Wenn du eine Datei startest, setzt die CLI das aktuelle Arbeitsverzeichnis auf den Ordner dieser Skriptdatei. Das ist wichtig fuer relative Importe, fuer Dateioperationen und fuer Plugin DLLs, die ueber relative Pfade geladen werden.

Die Importauflosung schaut anschliessend in dieser Reihenfolge.

1. Skriptverzeichnis.
2. Aktuelles Arbeitsverzeichnis.
3. Verzeichnis der ausgefuehrten CLI.

## Dateitypen

- `.cfs` ist das normale Quellformat.
- `.dll` kann per `import` als Plugin geladen werden.
- `.cfb` wird nicht mehr unterstuetzt. Die Binary Unterstuetzung wurde entfernt.

## Der kleinste sinnvolle Startpunkt

In fast allen praktischen Skripten importierst du zuerst die Standardbibliothek.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    print("CFGS laeuft");
}

main();
```

## Top Level Verhalten kurz erklaert

Top Level in CFGS ist bewusst streng. Dort gehoeren vor allem Importe, Deklarationen und identifierbasierte Statements hin. Kontrollfluss wie `if`, `while` oder `try` gehoert in Funktionen oder Methoden.

Das fuehrt oft zu einem einfachen Muster.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    if (true) {
        print("alles gut");
    }
}

main();
```

Wenn du verstehen willst, warum bestimmte Dinge am Top Level nicht erlaubt sind, lies als Naechstes [Sprache und Daten](02_sprache_und_daten.md) und [Kontrollfluss und Fehler](03_kontrollfluss_und_fehler.md).
