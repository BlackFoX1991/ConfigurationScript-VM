# _cfs_web Dokumentation

Diese Datei ist die vollstaendige Referenz fuer das Framework im aktuellen Stand des Ordners `_cfs_web`.

Sie beschreibt:

- das Grundmodell des Frameworks
- die wichtigen APIs von `ServerApp`, `RequestView`, `ResponseView`, `Middleware`, `Forms`, `Validation` und `Guards`
- typische Patterns fuer Sessions, Redirects, Uploads, Static Files und Fehlerseiten
- konkrete Codebeispiele
- die bekannten Grenzen der aktuellen Runtime und des HTTP-Plugins

Wenn du nur schnell sehen willst, wie alles zusammenspielt, lies zuerst:

- `example_app.cfs`
- `run_example_app.cfs`
- `USAGE_NOTES.md`

## 1. Ziel und Architektur

`_cfs_web` ist ein serverseitiges Web-Framework fuer CFS mit Fokus auf:

- klassische server-rendered HTML-Apps
- direkte API-Endpunkte mit JSON oder Text
- sichere Session- und Formular-Flows
- testbare Request-Verarbeitung ueber `app.handle(req)` ohne echten Listener

Das Framework ist in Schichten aufgebaut:

- `server.cfs` ist der HTTP-Kern mit Routing, Sessions, Request-/Response-Wrappern, Static-Handling und dem Live-Server-Loop.
- `middleware.cfs` liefert wiederverwendbare Before-/After-Middleware.
- `forms.cfs`, `validation.cfs` und `guards.cfs` bilden den App-Layer fuer Formulare, Validierung und Zugriffsschutz.
- `dom.cfs`, `layout.cfs`, `components.cfs` und `views.cfs` bilden eine kleine HTML-/View-Schicht fuer serverseitiges Rendering.
- `cookies.cfs`, `session_store.cfs`, `component_state.cfs` und `common.cfs` liefern Hilfslogik fuer State, Cookies, Uploads und HTTP-Metadaten.

## 2. Import-Muster

Wenn deine App ausserhalb dieses Ordners liegt, importierst du typischerweise so:

```cfs
import { ServerApp } from "_cfs_web/server.cfs";
import { Middleware } from "_cfs_web/middleware.cfs";
import { Forms } from "_cfs_web/forms.cfs";
import { Validation } from "_cfs_web/validation.cfs";
import { Guards } from "_cfs_web/guards.cfs";
import { Dom } from "_cfs_web/dom.cfs";
import { Layout } from "_cfs_web/layout.cfs";
import { ViewModels, Templates } from "_cfs_web/views.cfs";
```

Innerhalb des `_cfs_web`-Ordners selbst reichen relative Importe wie `server.cfs` oder `forms.cfs`.

Die Plugin-DLLs werden im Framework intern bereits ueber `plugins/...` importiert. App-Code muss deshalb normalerweise keine Debug- oder Dist-Pfade referenzieren.

## 3. Schnellstart

Das kleinste sinnvolle Beispiel besteht aus einem `ServerApp`, mindestens einer Route und einem Rueckgabewert ueber `ctx.response`.

```cfs
import { Dom } from "_cfs_web/dom.cfs";
import { Layout } from "_cfs_web/layout.cfs";
import { ViewModels, Templates } from "_cfs_web/views.cfs";
import { ServerApp } from "_cfs_web/server.cfs";

func home(ctx) {
    var vm = ViewModels.page(
        "Hello",
        "Minimal app",
        "Eine kleine HTML-Seite aus dem Framework."
    );

    return ctx.response.html(Templates.page(vm, Layout.panel([
        Dom.h("p", null, "Hallo aus _cfs_web.")
    ])));
}

async func main() {
    var app = new ServerApp(8092);
    app.get_named("home", "/", home);
    return await app.run();
}

var _ = await main();
```

Fuer direkte Tests ohne Listener kannst du dieselbe App ueber `handle(...)` aufrufen:

```cfs
var res = await app.handle({
    "method":"GET",
    "path":"/",
    "query":"",
    "headers":{"Host":"localhost:8092"},
    "body":""
});

print(res["status"]);
print(res["body"]);
```

## 4. Das Laufzeitmodell

Ein `ServerApp` verarbeitet Requests in dieser Reihenfolge:

1. Session laden oder neu anlegen
2. Request-Kontext `ctx` aufbauen
3. globale `use_before(...)`-Middleware ausfuehren
4. statische Mounts pruefen
5. Route suchen und Handler ausfuehren
6. automatische HTTP-Semantik anwenden, falls kein Handler getroffen wurde
7. globale `use_after(...)`-Middleware ausfuehren
8. Session persistieren
9. Response finalisieren

Wichtige Punkte:

- Handler, Middleware und Error-Handler duerfen synchron oder `async` sein.
- `HEAD`-Requests werden am Ende immer koerperlos finalisiert.
- `204` und `304` werden ebenfalls koerperlos normalisiert.
- Wenn keine explizite `HEAD`-Route existiert, faellt `HEAD` auf `GET` zurueck.
- Wenn ein Pfad existiert, aber die Methode nicht passt, erzeugt das Framework `405 Method Not Allowed` inklusive `Allow`-Header.
- Fuer bekannte Pfade werden automatische `OPTIONS`-Antworten erzeugt.

## 5. ServerApp im Detail

### 5.1 Erzeugen und Starten

```cfs
var app = new ServerApp(8094);
await app.run();
```

Wichtige Methoden:

| API | Bedeutung |
| --- | --- |
| `new ServerApp(port)` | erstellt die App und initialisiert den Default-Session-Store |
| `await app.run()` | startet den Live-Server-Loop ueber das HTTP-Plugin |
| `await app.handle(req)` | verarbeitet einen Request direkt im Speicher |
| `app.request_stop()` | fordert das Beenden des Live-Loops an |

`app.handle(req)` ist der wichtigste Einstieg fuer Smoke-Tests, weil er ohne echten Socket auskommt.

### 5.2 Routing

Basis-Methoden:

| API | Bedeutung |
| --- | --- |
| `app.route(method, pattern, handler, name = null, options = null)` | generische Registrierung |
| `app.get(...)`, `app.post(...)`, `app.put(...)`, `app.patch(...)`, `app.delete_(...)`, `app.head(...)`, `app.options(...)` | unbenannte Routen |
| `app.get_named(...)`, `app.post_named(...)`, `app.put_named(...)`, `app.patch_named(...)`, `app.delete_named(...)`, `app.head_named(...)`, `app.options_named(...)` | benannte Routen |
| `app.group(prefix, name_prefix = "")` | erstellt eine `RouteGroup` |

Pattern-Regeln:

- `/hello/:name` bindet `name` als Route-Parameter.
- `/files/*` erlaubt einen Wildcard-Restpfad.
- der Wildcard-Wert wird ueber `params["*"]` beim URL-Building wieder eingesetzt.

Beispiel:

```cfs
app.get_named("hello.show", "/hello/:name", func(ctx) {
    return ctx.response.text("Hello " + ctx.request.param("name", "guest"));
});

print(app.url("hello.show", {"name":"Ada"}));
```

### 5.3 Route-Optionen

Aktuell sind vor allem diese Optionen wichtig:

| Option | Bedeutung |
| --- | --- |
| `{"csrf": false}` | schaltet die Formular-CSRF-Pruefung fuer diese Route aus |
| `{"max_body_bytes": 4096}` | setzt ein lokales Request-Body-Limit fuer diese Route |

Beispiel:

```cfs
app.post("/webhook", func(ctx) {
    return ctx.response.text("ok");
}, {
    "csrf": false,
    "max_body_bytes": 16384
});
```

### 5.4 RouteGroup

`RouteGroup` ist fuer Prefixe, Namensraeume und gruppenlokale Before-/After-Middleware gedacht.

Wichtige Methoden:

| API | Bedeutung |
| --- | --- |
| `group.use_before(f)` | Before-Middleware nur fuer diese Gruppe |
| `group.use_after(f)` | After-Middleware nur fuer diese Gruppe |
| `group.get(...)`, `group.post(...)`, ... | wie bei `ServerApp`, aber mit Prefix |
| `group.get_named(...)`, `group.post_named(...)`, ... | wie bei `ServerApp`, aber mit Name-Prefix |
| `group.group(prefix, name_prefix = null)` | verschachtelte Gruppen, inklusive geerbter Group-Middleware |

Beispiel:

```cfs
var auth = app.group("/auth", "auth");

auth.post_named("login", "/login", login);
auth.post_named("logout", "/logout", logout);

print(app.url("auth.login"));
```

## 6. Der Request-Kontext `ctx`

Jeder Handler bekommt ein Dictionary `ctx`. Die wichtigsten direkten Felder sind:

| Feld | Bedeutung |
| --- | --- |
| `ctx.app` | aktuelle `ServerApp` |
| `ctx.req` | roher eingehender Request |
| `ctx.session` | Session-Dictionary der aktuellen Anfrage |
| `ctx.shared` | prozessweiter Shared-State der App |
| `ctx.locals` | request-lokale Daten fuer Middleware/Handler |
| `ctx.params` | Route-Parameter |
| `ctx.query` | geparste Query-Werte |
| `ctx.form` | geparste Form-/JSON-/Text-Daten |
| `ctx.uploads` | Upload-Dictionary fuer Multipart-Requests |
| `ctx.flash` | One-Request-Flash-Werte |
| `ctx.messages` | persistente Session-Messages |
| `ctx.old` | gespeicherte alte Formwerte aus dem letzten Redirect |
| `ctx.old_errors` | gespeicherte Feldfehler aus dem letzten Redirect |
| `ctx.request` | `RequestView` mit bequemen Reader-Methoden |
| `ctx.response` | `ResponseView` mit Builder- und Session-Helfern |

### 6.1 RequestView: Metadaten

| API | Bedeutung |
| --- | --- |
| `ctx.request.method()` | HTTP-Methode |
| `ctx.request.path()` | Request-Pfad |
| `ctx.request.remote(fallback = null)` | roher Remote-Wert |
| `ctx.request.client_ip(fallback = null)` | aufgeloeste Client-IP unter Beruecksichtigung vertrauenswuerdiger Proxies |
| `ctx.request.host(fallback = null)` | aufgeloester Host |
| `ctx.request.scheme(fallback = "http")` | `http` oder `https` |
| `ctx.request.origin(fallback = null)` | kombinierter Origin-Wert |
| `ctx.request.is_secure()` | true bei `https` |
| `ctx.request.trusted_proxy()` | true, wenn der direkte Remote als vertrauenswuerdig konfiguriert ist |

### 6.2 RequestView: Datenzugriff

| API | Bedeutung |
| --- | --- |
| `ctx.request.param(name, fallback = null)` | Route-Parameter |
| `ctx.request.query(name, fallback = null)` | Query-Wert |
| `ctx.request.form(name, fallback = null)` | Form-/Body-Wert |
| `ctx.request.uploads(name = null, fallback = null)` | alle Uploads oder Uploads eines Felds |
| `ctx.request.upload(name, fallback = null)` | erster Upload eines Felds |
| `ctx.request.cookie(name, fallback = null)` | normaler Cookie-Wert |
| `ctx.request.signed_cookie(name, secret, fallback = null)` | signierter Cookie-Wert nach MAC-Pruefung |

### 6.3 RequestView: Framework-Helfer

| API | Bedeutung |
| --- | --- |
| `ctx.request.url(name, params = null, query = null)` | URL aus benannter Route bauen |
| `ctx.request.asset(path, version = null, query = null)` | Asset-URL mit optionalem Version-Parameter |
| `ctx.request.csrf_field_name()` | Name des CSRF-Formfelds |
| `ctx.request.csrf_token()` | Session-CSRF-Token |
| `ctx.request.local(name, fallback = null)` | Wert aus `ctx.locals` |
| `ctx.request.flash(name = null, fallback = null)` | Flash-Werte lesen |
| `ctx.request.session_messages(name = null, fallback = null)` | persistente Session-Messages lesen |
| `ctx.request.session_message(name, fallback = null)` | einzelne Session-Message lesen |
| `ctx.request.old(name = null, fallback = null)` | alte Eingabewerte lesen |
| `ctx.request.old_errors(fallback = null)` | alte Feldfehler lesen |
| `ctx.request.old_error(name, fallback = null)` | einzelnen alten Feldfehler lesen |
| `ctx.request.session(name, fallback = null)` | Session-Wert lesen |
| `ctx.request.shared(name, fallback = null)` | Shared-State lesen |

### 6.4 RequestView: Methodenabfragen

Es gibt fuer Handler einfache Methodenchecks:

- `ctx.request.is_get()`
- `ctx.request.is_post()`
- `ctx.request.is_put()`
- `ctx.request.is_patch()`
- `ctx.request.is_delete()`
- `ctx.request.is_head()`
- `ctx.request.is_options()`

## 7. ResponseView

`ctx.response` erzeugt Response-Dictionaries und kapselt Redirect-, Cookie- und Session-Helfer.

### 7.1 Basis-Builder

| API | Bedeutung |
| --- | --- |
| `ctx.response.response(status, body, content_type = "text/plain; charset=utf-8", headers = null)` | generische Response |
| `ctx.response.text(body, status = 200, headers = null)` | Text-Response |
| `ctx.response.html(body, status = 200, headers = null)` | HTML-Response |
| `ctx.response.json(value, status = 200, headers = null)` | JSON-Response |
| `ctx.response.error_page(status = 500, title = null, lead = null, detail = null, headers = null)` | HTML-Fehlerseite |
| `ctx.response.debug_page(err, status = 500, title = null, lead = null, headers = null)` | HTML-Debugseite |

Beispiel:

```cfs
app.get("/health", func(ctx) {
    return ctx.response.json({
        "ok": true,
        "service": "api"
    });
});
```

### 7.2 Redirects

| API | Bedeutung |
| --- | --- |
| `ctx.response.redirect(location, status = 303)` | Redirect auf absolute oder relative URL |
| `ctx.response.redirect_to(name, params = null, query = null, status = 303)` | Redirect ueber benannte Route |
| `ctx.response.back(fallback = "/", status = 302)` | sicherer Redirect ueber `Referer`, nur same-host oder relativ |

`back(...)` ist absichtlich restriktiv, damit keine offenen Redirects ueber fremde Hosts entstehen.

### 7.3 Cookies

| API | Bedeutung |
| --- | --- |
| `ctx.response.with_cookie(res, name, value, attrs = null)` | fuegt `Set-Cookie` an bestehende Response an |
| `ctx.response.with_signed_cookie(res, name, value, secret, attrs = null)` | fuegt signierten Cookie an |
| `ctx.response.expire_cookie(res, name, attrs = null)` | setzt Cookie mit `Max-Age=0` ablaufend |

Typisches Pattern:

```cfs
var res = ctx.response.redirect_to("home");
return ctx.response.with_signed_cookie(res, "workspace", "planning-lab", "secret", {
    "Path":"/",
    "SameSite":"Lax"
});
```

### 7.4 Session- und Redirect-Helfer

| API | Bedeutung |
| --- | --- |
| `ctx.response.rotate_session()` | neue Session-ID und neues CSRF-Token |
| `ctx.response.flash(name, value)` | One-Request-Message speichern |
| `ctx.response.session_message(name, value)` | persistente Session-Message speichern |
| `ctx.response.clear_session_message(name = null)` | eine oder alle Session-Messages entfernen |
| `ctx.response.remember_old_input(values = null, errors = null)` | alte Eingaben und Fehler fuer den naechsten Request speichern |
| `ctx.response.remember_session(seconds = null)` | Session-Cookie persistent machen |
| `ctx.response.forget_session()` | persistenten Session-Cookie wieder zum Browser-Session-Cookie machen |
| `ctx.response.logout_session(fresh = true)` | Session entfernen und optional frische leere Session anlegen |

Es gibt ausserdem zusammengesetzte Redirect-Helfer:

- `ctx.response.redirect_with_flash(...)`
- `ctx.response.redirect_to_with_flash(...)`
- `ctx.response.redirect_with_session_message(...)`
- `ctx.response.redirect_to_with_session_message(...)`
- `ctx.response.redirect_with_input(...)`
- `ctx.response.redirect_to_with_input(...)`

PRG-Beispiel:

```cfs
if (!form.is_valid()) {
    return ctx.response.redirect_to_with_input("home", {
        "title": form.value("title", "")
    }, form.field_errors_map());
}

ctx.response.flash("notice", "Eintrag gespeichert.");
return ctx.response.redirect_to("home");
```

### 7.5 Dateien und Downloads

| API | Bedeutung |
| --- | --- |
| `await ctx.response.file(path, content_type = null, headers = null)` | Textdatei inline ausliefern |
| `await ctx.response.download(path, download_name = null, content_type = null, headers = null)` | Textdatei als Download ausliefern |

Wichtig:

- der aktuelle HTTP-Plugin-Pfad transportiert Responses nur als Strings
- deshalb funktionieren hier aktuell nur textartige Dateitypen sinnvoll
- fuer echte binaere Assets braucht die Runtime Byte-Transport

## 8. Request-Parsing

Das Framework parst den Body automatisch anhand des `Content-Type`:

| Content-Type | Ergebnis |
| --- | --- |
| `application/x-www-form-urlencoded` | Werte in `ctx.form` |
| `multipart/form-data` | normale Felder in `ctx.form`, Uploads in `ctx.uploads` |
| `application/json` | JSON-Wert in `ctx.form` |
| `text/plain` | Rohtext unter `ctx.form["_raw"]` und `ctx.form["_text"]` |
| sonstige Typen | Rohtext unter `ctx.form["_raw"]` |

### 8.1 Upload-Modell

Ein Upload-Eintrag aus `ctx.request.upload("attachment")` sieht so aus:

```cfs
{
    "name":"attachment",
    "field_name":"attachment",
    "filename":"spec.txt",
    "original_filename":"spec.txt",
    "content_type":"text/plain",
    "body":"release spec",
    "size":12,
    "headers":{ ... }
}
```

Wenn ein Feld mehrfach vorkommt, wird der Dictionary-Eintrag zu einem Array erweitert. Deshalb gilt:

- `ctx.request.upload(name)` gibt den ersten Treffer zurueck
- `ctx.request.uploads(name)` gibt den Rohwert zurueck, also Einzeleintrag oder Array

## 9. Sessions und Cookies

### 9.1 Default-Verhalten

Beim ersten Request wird automatisch eine Session angelegt. Der Default umfasst:

- kryptographisch zufaellige Session-ID
- serverseitige Idle- und Absolute-Timeouts
- `HttpOnly` und `SameSite=Lax` fuer den Session-Cookie
- Browser-Session-Cookie statt persistentem Cookie
- automatisches CSRF-Token in der Session
- automatischen Session-Zaehler `visits`

### 9.2 Relevante Konfiguration

| API | Bedeutung |
| --- | --- |
| `app.set_session_cookie_secure(true)` | setzt `Secure` auf dem Session-Cookie |
| `app.set_session_cookie_persistent(true)` | verwendet standardmaessig persistente Session-Cookies |
| `app.set_session_timeouts(idleSeconds, absoluteSeconds)` | setzt Idle- und Absolute-Timeouts |
| `app.set_session_cookie_signing_secret(secret)` | signiert und verifiziert den Session-Cookie |
| `app.set_session_store(store)` | ersetzt den Default-Store |
| `app.set_csrf_enabled(false)` | schaltet formularbasierte CSRF-Pruefung global ab |

Beispiel:

```cfs
app.set_session_cookie_signing_secret("dev-session-secret");
app.set_session_cookie_secure(true);
app.set_session_timeouts(1800, 43200);
```

### 9.3 Signed Cookies

Es gibt aktuell signierte, aber nicht verschluesselte Cookies.

Das bedeutet:

- der Browserinhalt bleibt lesbar
- Manipulation wird ueber HMAC erkannt
- fuer Geheimnisse ist das kein Ersatz fuer Verschluesselung

Die zugrundeliegenden Helfer liegen in `cookies.cfs`:

| API | Bedeutung |
| --- | --- |
| `Cookies.sign_value(name, value, secret)` | Wert signieren |
| `Cookies.unsign_value(name, value, secret, fallback = null)` | Wert pruefen und entschluesseln |
| `Cookies.read_signed(ctx, name, secret, fallback = null)` | signierten Cookie lesen |
| `Cookies.with_signed_cookie(res, name, value, secret, attrs = null)` | signierten Cookie setzen |

### 9.4 Session-Lifecycle

Wichtige Flows:

- `ctx.response.rotate_session()` nach einem Auth-Event
- `ctx.response.remember_session(seconds)` fuer Remember-Me-Cookies
- `ctx.response.forget_session()` fuer Rueckkehr zum Browser-Session-Cookie
- `ctx.response.logout_session()` fuer Logout und frische leere Session

Beispiel:

```cfs
ctx.session["user_id"] = "usr_123";
ctx.response.rotate_session();
ctx.response.remember_session(604800);
return ctx.response.redirect_to("home");
```

### 9.5 Pluggable Session Store

Der Default-Store ist `MemorySessionStore`. Ein eigener Store muss diese Methoden anbieten:

- `load(sid)`
- `save(sid, entry)`
- `forget(sid)`
- `prune(now, idle_seconds = null, absolute_seconds = null)`
- `snapshot()`

Ein Session-Entry hat dieselbe Struktur wie intern im Framework:

```cfs
{
    "id":"sid_...",
    "data":{ ...session values... },
    "created_at": 1711960000,
    "last_seen_at": 1711960042
}
```

Ein minimaler eigener Store:

```cfs
export class MySessionStore
{
    var entries = {};

    func load(sid) {
        return this.entries[sid];
    }

    func save(sid, entry) {
        this.entries[sid] = entry;
        return entry;
    }

    func forget(sid) {
        this.entries[sid] = null;
        return 1;
    }

    func prune(now, idle_seconds = null, absolute_seconds = null) {
        return this.entries;
    }

    func snapshot() {
        return this.entries;
    }
}

app.set_session_store(new MySessionStore());
```

## 10. Flash, Session Messages und Old Input

Es gibt drei unterschiedliche State-Arten fuer UI-Feedback:

| Mechanismus | Lebensdauer | Typischer Zweck |
| --- | --- | --- |
| Flash | genau der naechste Request | "Gespeichert", "Abgemeldet", Fehlerhinweis nach Redirect |
| Session Message | persistent bis geloescht | Banner, langfristige Hinweise |
| Old Input | genau der naechste Request | Formwerte und Feldfehler nach Redirect |

Typisches Muster:

```cfs
if (!form.is_valid()) {
    return ctx.response.redirect_to_with_input("home", form.field_values_map(), form.field_errors_map());
}

ctx.response.flash("notice", "Gespeichert.");
ctx.response.session_message("banner", "Du arbeitest gerade im Admin-Modus.");
return ctx.response.redirect_to("home");
```

Im GET-Handler oder Template:

```cfs
Forms.flash_message(ctx, "notice");
Forms.session_message(ctx, "banner");
Forms.text_field_from_ctx(ctx, "Title", "title");
```

## 11. Formulare und Validierung

### 11.1 `Validation`

`validation.cfs` liefert ein kleines Binding- und Regelmodell ueber `BoundForm`.

Wichtige Reader:

| API | Bedeutung |
| --- | --- |
| `form.text(name, fallback = "", trim = true)` | liest Textwert |
| `form.raw(name, fallback = null)` | liest Rohwert |
| `form.bool(name, fallback = false)` | liest Checkbox-/Bool-Wert |

Wichtige Regeln:

| API | Bedeutung |
| --- | --- |
| `form.require(name, message = null)` | Pflichtfeld |
| `form.min_len(name, min_len, message = null)` | Mindestlaenge |
| `form.max_len(name, max_len, message = null)` | Maximallaenge |
| `form.one_of(name, allowed, message = null)` | Wert muss in Liste enthalten sein |

Wichtige Reader fuer Ergebnisse:

| API | Bedeutung |
| --- | --- |
| `form.value(name, fallback = null)` | normalisierter Feldwert |
| `form.field_values_map()` | alle gelesenen Werte |
| `form.field_errors_map()` | alle Feldfehler |
| `form.error(name, fallback = null)` | Fehler eines Felds |
| `form.has_error(name)` | Feld hat Fehler |
| `form.is_valid()` | keine Fehler vorhanden |

Typisches Pattern:

```cfs
var form = Validation.from_request(ctx);
form.text("title", "");
form.text("details", "");
form.bool("pinned", false);
form.require("title", "Bitte einen Titel eingeben.");
form.min_len("title", 3, "Titel muss mindestens 3 Zeichen haben.");
form.max_len("details", 200, "Details duerfen hoechstens 200 Zeichen haben.");

if (!form.is_valid()) {
    return ctx.response.redirect_to_with_input("home", {
        "title": form.value("title", ""),
        "details": form.value("details", ""),
        "pinned": form.raw("pinned", null)
    }, form.field_errors_map());
}
```

### 11.2 `Forms`

`forms.cfs` baut auf `Dom` und `Ui` auf und liefert vor allem formularzentrierte HTML-Helfer.

Wichtige Builder:

| API | Bedeutung |
| --- | --- |
| `Forms.csrf_field(ctx)` | verstecktes CSRF-Feld |
| `Forms.text_field(...)` | Textfeld |
| `Forms.textarea_field(...)` | Textarea |
| `Forms.checkbox_field(...)` | Checkbox |
| `Forms.radio_field(...)` | einzelnes Radio |
| `Forms.radio_group(...)` | Radio-Gruppe |
| `Forms.select_field(...)` | Select |
| `Forms.file_field(...)` | Datei-Input |
| `Forms.error_summary(messages, title = "...")` | Formfehler-Box |
| `Forms.flash_message(ctx, name, class_name = "flash")` | Flash-Baustein |
| `Forms.session_message(ctx, name, class_name = "flash session-message")` | persistente Message |
| `Forms.event_button(action, label, btn_class = "btn", extra_fields = null, ctx = null)` | POST-Button-Form mit optionalem CSRF |
| `Forms.text_post(...)` | kleines Formular fuer einen einzelnen Text-POST |

Zusatz fuer Old Input:

| API | Bedeutung |
| --- | --- |
| `Forms.old_value(ctx, field_name, fallback = "")` | alter Feldwert |
| `Forms.old_error(ctx, field_name, fallback = null)` | alter Feldfehler |
| `Forms.old_checked(ctx, field_name, checked_value = "1", fallback = false)` | alter Checkbox-/Radio-Status |
| `Forms.text_field_from_ctx(...)` | Textfeld mit `old` + `old_error` |
| `Forms.textarea_field_from_ctx(...)` | Textarea mit `old` + `old_error` |
| `Forms.checkbox_field_from_ctx(...)` | Checkbox mit `old` + `old_error` |
| `Forms.radio_group_from_ctx(...)` | Radio-Gruppe mit `old` + `old_error` |
| `Forms.select_field_from_ctx(...)` | Select mit `old` + `old_error` |

Praxisbeispiel:

```cfs
Ui.form(ctx.request.url("ideas.create"), "POST", [
    Forms.csrf_field(ctx),
    Forms.text_field_from_ctx(ctx, "Title", "title", "", "Release checklist"),
    Forms.textarea_field_from_ctx(ctx, "Details", "details"),
    Forms.select_field_from_ctx(ctx, "Category", "category", {
        "product":"Product",
        "ops":"Ops"
    }, "product", "ideaCategory", "Choose a category"),
    Forms.checkbox_field_from_ctx(ctx, "Pin item", "pinned", "1", false),
    Forms.file_field("Attachment", "attachment", "ideaAttachment", null, ".txt,.md"),
    Forms.button_row([
        Ui.submit("Save")
    ])
], {
    "class":"stack",
    "enctype":"multipart/form-data"
});
```

## 12. Guards und Zugriffsschutz

`guards.cfs` kapselt haeufige Session- und Auth-Checks.

Wichtige APIs:

| API | Bedeutung |
| --- | --- |
| `Guards.protect(handler, predicate, on_fail = null)` | generischer Wrapper |
| `Guards.require_session_flag(key, handler, redirect_to = "/", expected = true)` | einfacher Bool-Flag-Check |
| `Guards.require_session_key(key, handler, redirect_to = "/", allow_blank = false, status = 302)` | Session-Key muss vorhanden sein |
| `Guards.require_authenticated(handler, redirect_to = "/login", session_key = "user_id", allow_blank = false, status = 302)` | Auth-Check |
| `Guards.require_guest(handler, redirect_to = "/", session_key = "user_id", allow_blank = false, status = 302)` | Guest-Check |
| `Guards.require_role(role_or_roles, handler, redirect_to = "/", session_key = "role", status = 302)` | Rollen-Check |
| `Guards.require_session_flags_all(keys, handler, redirect_to = "/", expected = true, status = 302)` | mehrere Flags muessen passen |
| `Guards.require_session_flags_any(keys, handler, redirect_to = "/", expected = true, status = 302)` | mindestens ein Flag muss passen |

Beispiel:

```cfs
app.get_named("admin.home", "/admin",
    Guards.require_role("admin", func(ctx) {
        return ctx.response.text("ok");
    }, "/")
);
```

## 13. Middleware

### 13.1 Signaturen

Before-Middleware:

```cfs
func(ctx) {
    return null;
}
```

Wenn eine Before-Middleware eine Response zurueckgibt, wird der Rest der Pipeline fuer diesen Request uebersprungen.

After-Middleware:

```cfs
func(ctx, res) {
    return res;
}
```

Error-Middleware:

```cfs
func(ctx, err) {
    return ctx.response.text("fail", 500);
}
```

### 13.2 Eingebaute Middleware

| API | Bedeutung |
| --- | --- |
| `Middleware.request_logger(prefix = null)` | loggt `METHOD PATH` nach stdout |
| `Middleware.counter(shared_key = "request_count", local_key = null)` | zaehlt Requests im `shared`-State |
| `Middleware.header(name, value)` | setzt After-Header |
| `Middleware.no_store()` | setzt `Cache-Control: no-store` |
| `Middleware.security_headers(overrides = null)` | setzt Default-Sicherheitsheader, ohne vorhandene explizite Header zu ueberschreiben |
| `Middleware.rate_limit(limit, window_seconds, key_func = null, options = null)` | Fixed-Window-Limiter |
| `Middleware.rate_limit_headers(local_key = "__rate_limit")` | emittiert `X-RateLimit-*` und optional `Retry-After` |
| `Middleware.host_policy(allowed_hosts, options = null)` | prueft den aufgeloesten Host |
| `Middleware.origin_policy(allowed_origins = null, options = null)` | prueft `Origin` oder `Referer` fuer unsichere Methoden |

### 13.3 Security-Header-Defaults

`Middleware.security_headers()` setzt standardmaessig:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- eine restriktive `Content-Security-Policy`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`

Wenn dein Handler denselben Header bereits explizit gesetzt hat, ueberschreibt die Middleware ihn nicht.

### 13.4 Rate Limiting

`Middleware.rate_limit(...)` speichert den Zustand in `ctx.locals[local_key]`. Die wichtigsten Optionen sind:

| Option | Default | Bedeutung |
| --- | --- | --- |
| `shared_key` | `__rate_limit_store` | globaler Key im Shared-State |
| `local_key` | `__rate_limit` | Key in `ctx.locals` |
| `status` | `429` | Statuscode bei Blockierung |
| `message` | `Too Many Requests` | Response-Body bei Blockierung |
| `now_func` | interne UTC-Seconds | Test-Hook fuer Zeitsteuerung |

Empfohlenes Pattern:

```cfs
app.use_before(Middleware.rate_limit(120, 60));
app.use_after(Middleware.rate_limit_headers());
```

### 13.5 Host- und Origin-Policies

`Middleware.host_policy(...)` arbeitet auf dem aufgeloesten Request-Host.

Zulaessige Pattern:

- exakter Host: `app.example.test`
- exakter Host mit Port: `app.example.test:8443`
- Host mit beliebigem Port: `app.example.test:*`
- Wildcard-Subdomains: `*.example.test`

`Middleware.origin_policy(...)` prueft `Origin` oder optional `Referer`.

Wichtige Optionen:

| Option | Default | Bedeutung |
| --- | --- | --- |
| `status` | `403` | Fehlerstatus |
| `message` | `Forbidden` | Fehlertext |
| `unsafe_only` | `true` | nur fuer `POST`, `PUT`, `PATCH`, `DELETE` |
| `allow_missing` | `false` | fehlende Origin/Referer-Werte erlauben |
| `allow_referer` | `true` | `Referer` als Fallback akzeptieren |

## 14. Reverse Proxies und Request-Metadaten

Der Framework-Kern kann `X-Forwarded-For`, `X-Forwarded-Host` und `X-Forwarded-Proto` auswerten, aber nur fuer explizit freigegebene direkte Proxies.

Konfiguration:

```cfs
app.set_trusted_proxies(["127.0.0.1", "::1"]);
```

Danach liefern folgende Reader aufgeloeste Werte:

- `ctx.request.client_ip()`
- `ctx.request.host()`
- `ctx.request.scheme()`
- `ctx.request.origin()`
- `ctx.request.trusted_proxy()`

Wichtig:

- nur direkte Proxies freigeben, die du kontrollierst
- erst mit korrekter Proxy-Konfiguration stimmen `scheme()` und `origin()` hinter TLS-Offloading
- `host_policy(...)` und `origin_policy(...)` arbeiten auf diesen aufgeloesten Werten

## 15. Static Files, Assets und Downloads

### 15.1 Static-Mounts

Es gibt drei Static-Varianten:

| API | Bedeutung |
| --- | --- |
| `app.serve_static_with(prefix, loader_func, max_age = 3600)` | generischer Loader |
| `app.serve_static_map(prefix, files, max_age = 3600)` | In-Memory-Map |
| `app.serve_static_dir(prefix, root_dir, max_age = 3600)` | Verzeichnis fuer textartige Assets |

Der Loader fuer `serve_static_with(...)` kann entweder:

- direkt einen String zurueckgeben
- oder ein Dictionary mit Metadaten

Unterstuetzte Loader-Felder:

| Feld | Bedeutung |
| --- | --- |
| `data` oder `body` | Response-Body |
| `content_type` | Content-Type |
| `status` | optionaler Status |
| `headers` | zusaetzliche Header |
| `etag` | explizites ETag |
| `last_modified` | explizite Last-Modified-Metadaten |

Beispiel:

```cfs
app.serve_static_map("/assets/", {
    "example.css": {
        "data":"body{background:#f7f0e3}",
        "content_type":"text/css; charset=utf-8"
    }
}, 900);
```

### 15.2 HTTP-Caching

Fuer statische Responses und Datei-Responses gilt:

- `ETag` wird automatisch erzeugt, wenn keiner gesetzt ist
- `If-None-Match` wird ausgewertet
- `Last-Modified` wird ausgewertet, wenn explizite Metadaten oder Header vorhanden sind
- `If-Modified-Since` wird dann ebenfalls ausgewertet

Die automatische Dateitimestamp-Ermittlung aus dem Dateisystem ist aktuell noch nicht moeglich, weil die Standardbibliothek diese Information hier nicht direkt bereitstellt.

### 15.3 Asset-URLs

Fuer Cache-Busting gibt es:

| API | Bedeutung |
| --- | --- |
| `app.set_asset_version(version)` | globale Asset-Version |
| `app.set_asset_versions(values)` | per-Asset-Versionen |
| `app.asset_url(path, version = null, query = null)` | generische URL-Erzeugung |
| `ctx.request.asset(path, version = null, query = null)` | bequemer Reader im Handler/Template |

Beispiel:

```cfs
app.set_asset_version("example-2026-04-01");
print(app.asset_url("/assets/example.css"));
```

## 16. Fehlerseiten und Debug-Seiten

Es gibt zwei Ebenen:

- explizite HTML-Fehlerseiten ueber `ctx.response.error_page(...)` und `ctx.response.debug_page(...)`
- automatische Default-Fehlerseiten fuer `403`, `404`, `405`, `413` und `500`

Die Default-Seiten verhalten sich so:

- bei `Accept: text/html` kommt HTML
- ohne explizites HTML-Accept bleibt das Verhalten textbasiert

Das ist absichtlich so, damit Browser eine schoene Fehlerseite sehen, aber textbasierte Smoke-Tests stabil bleiben.

Beispiel fuer einen eigenen Error-Handler:

```cfs
func render_error(ctx, err) {
    if (ctx.app.debug_errors) {
        return ctx.response.debug_page(
            err,
            500,
            "Example error",
            "Debug-Details fuer die Entwicklung."
        );
    }

    return ctx.response.error_page(
        500,
        "Example error",
        "Eine freundliche HTML-Fehlerseite fuer den Nutzer.",
        "Aktiviere Debug-Errors nur lokal."
    );
}

app.use_error(render_error);
```

Wenn kein eigener Error-Handler etwas zurueckgibt, faellt das Framework auf seine Default-Seiten zurueck.

## 17. CSRF-Schutz

CSRF ist fuer browserartige Formular-Requests auf unsicheren Methoden standardmaessig aktiv.

Das bedeutet:

- `POST`, `PUT`, `PATCH` und `DELETE` werden geprueft
- geprueft wird nur fuer Form-/Multipart-artige Browser-Requests
- JSON- oder Text-API-Endpunkte koennen ohne Formular-CSRF arbeiten

Reader und Builder:

| API | Bedeutung |
| --- | --- |
| `ctx.request.csrf_field_name()` | Feldname |
| `ctx.request.csrf_token()` | Token |
| `Forms.csrf_field(ctx)` | fertiges Hidden-Input |

Beispiel:

```cfs
Ui.form(ctx.request.url("auth.login"), "POST", [
    Forms.csrf_field(ctx),
    Forms.text_field("Name", "login_name"),
    Ui.submit("Sign in")
], {"class":"stack"});
```

Wenn du den Schutz gezielt deaktivieren willst:

```cfs
app.post("/webhook", webhook_handler, {"csrf": false});
```

## 18. View- und HTML-Schicht

### 18.1 `Dom`, `Ui` und `Html`

`dom.cfs` ist die niedrigste Render-Schicht.

Wichtige APIs:

| API | Bedeutung |
| --- | --- |
| `Dom.h(tag, props, children)` | virtuellen Knoten bauen |
| `Dom.render(node)` | VNode in HTML rendern |
| `Ui.form(...)`, `Ui.submit(...)`, `Ui.button_form(...)`, `Ui.text_input(...)`, `Ui.label(...)` | kleine Form-Helfer |
| `Html.page(title, body_node, extra_head = null)` | komplettes HTML-Dokument mit Base-CSS |

### 18.2 `Layout` und `Components`

`layout.cfs` liefert semantische Layout-Bloecke:

- `Layout.stack(...)`
- `Layout.row(...)`
- `Layout.panel(...)`
- `Layout.grid(...)`
- `Layout.hero(...)`

`components.cfs` liefert kleine wiederverwendbare UI-Bausteine:

- `Components.metric(...)`
- `Components.session_line(...)`
- `Components.code(...)`
- `Components.log_box(...)`

### 18.3 `ViewModels` und `Templates`

`views.cfs` bildet eine kleine View-Model-Schicht fuer Seiten und Dashboards.

Wichtige APIs:

| API | Bedeutung |
| --- | --- |
| `ViewModels.page(title, eyebrow = "", lead = "", meta = null)` | einfaches Seitenmodell |
| `ViewModels.dashboard(title, eyebrow = "", lead = "", meta = null)` | Dashboard-Modell mit `main` und `aside` |
| `ViewModels.set_slot(model, name, content)` | Slot hart setzen |
| `ViewModels.push_slot(model, name, content)` | Slot erweitern |
| `Templates.page(model, content)` | Seite rendern |
| `Templates.sections(model, sections)` | mehrere Sections rendern |
| `Templates.dashboard(model)` | Dashboard mit Main/Aside rendern |
| `Templates.with_sidebar(model, main, aside)` | explizite Sidebar-Struktur |
| `Templates.section(title, content, lead = null)` | einzelne Layout-Section |

Beispiel:

```cfs
var vm = ViewModels.dashboard(
    "Workshop Board",
    "Example",
    "Dashboard mit Main- und Aside-Bereich."
);

vm["main"] = [
    Layout.panel([Dom.h("p", null, "Main content")])
];

vm["aside"] = [
    Components.log_box("Meta", "Aside content")
];

return ctx.response.html(Templates.dashboard(vm));
```

### 18.4 `ComponentState`

`component_state.cfs` speichert serverseitigen UI-State in der Session.

Wichtige APIs:

| API | Bedeutung |
| --- | --- |
| `ComponentState.scope(ctx, component_id, defaults = null)` | State-Scope holen oder erzeugen |
| `ComponentState.read(ctx, component_id, key, fallback = null)` | Wert lesen |
| `ComponentState.write(ctx, component_id, key, value)` | Wert setzen |
| `ComponentState.patch(ctx, component_id, values)` | mehrere Werte setzen |
| `ComponentState.reset(ctx, component_id)` | Scope zuruecksetzen |

Beispiel:

```cfs
var state = ComponentState.scope(ctx, "filters", {
    "sort":"created"
});

state["sort"] = "priority";
```

## 19. Direkte Tests mit `handle(...)`

Die meisten Smoke-Tests in diesem Ordner laufen ueber `app.handle(req)`, weil das schneller und stabiler ist als ein echter Listener.

Ein minimaler Test-Request sieht so aus:

```cfs
func request(method, path, body = "", headers = null, remote = null) {
    return {
        "method": method,
        "path": path,
        "query": "",
        "headers": headers == null ? {"Host":"localhost:8094"} : headers,
        "body": body,
        "remote": remote
    };
}
```

Du kannst damit pruefen:

- Statuscodes
- Header
- Body
- Session-Cookies
- Flash-Verhalten
- Proxy-Aufloesung
- Uploads
- Error Pages

Das ist die bevorzugte Teststrategie, solange der Live-Listener in der Runtime auf diesem System blockiert ist.

## 20. Die Beispiel-App

`example_app.cfs` ist die beste Referenz fuer einen realistischeren Einsatz.

Sie zeigt in einer App:

- benannte Routen und Gruppen
- Sessions, Remember-Me und Logout
- signierte Session-Cookies und einen zusaetzlichen signierten Helper-Cookie
- Guards fuer Guest-, Auth- und Rollen-Checks
- Formular-Validierung mit Redirect-Back ueber Old Input
- Multipart-Uploads
- Download-Helfer
- Rate-Limiting und Security-Header
- Trusted-Proxies und aufgeloeste Request-Metadaten
- HTML-Fehlerseiten und Debug-Seiten

Die wichtigsten Bestandteile:

- `build_example_app(port = 8094)` baut die komplette App
- `run_example_app.cfs` startet sie direkt
- `phase15_example_app_smoke.cfs` prueft die App ohne echten Listener

Wenn du das Framework auf eine neue App uebertragen willst, ist das der beste Startpunkt.

## 21. Empfehlungen fuer echte Apps

Fuer normale Apps haben sich diese Defaults bewaehrt:

```cfs
app.set_session_cookie_signing_secret("replace-me");
app.set_session_timeouts(1800, 43200);
app.set_max_body_bytes(65536);
app.set_trusted_proxies(["127.0.0.1", "::1"]);

app.use_before(Middleware.rate_limit(120, 60));
app.use_after(Middleware.rate_limit_headers());
app.use_after(Middleware.security_headers());
```

Empfehlungen:

- signiere Session-Cookies immer in nicht-trivialen Apps
- setze `Secure` fuer echte HTTPS-Deployments
- nutze `redirect_to_with_input(...)` fuer POST -> Redirect -> GET
- verwende `host_policy(...)` und `origin_policy(...)` am Deployment-Rand
- nutze `handle(...)` fuer schnelle Tests und `run()` nur dort, wo die Runtime den Listener sauber oeffnen kann

## 22. Bekannte Grenzen

### 22.1 String-only HTTP-Transport

Der installierte HTTP-Plugin-Pfad transportiert Response-Bodies aktuell als Strings.

Praktische Folgen:

- `serve_static_dir(...)` ist fuer textartige Assets geeignet
- `ctx.response.file(...)` und `ctx.response.download(...)` sind fuer Textdateien geeignet
- echte binaere Dateien wie reale `png`, `jpg`, `pdf` oder beliebige Byte-Streams brauchen Runtime-/Plugin-Support

### 22.2 Keine automatischen Dateitimestamps

Die aktuelle Standardbibliothek liefert hier keine bequeme Dateitimestamp-API fuer das Framework.

Praktische Folge:

- `Last-Modified` funktioniert fuer explizite Metadaten oder Header
- `serve_static_dir(...)` und `ctx.response.file(...)` koennen Dateitimestamps nicht automatisch aus dem Dateisystem ableiten

### 22.3 Live-Listener der Runtime

Die direkte `app.run()`-Verifikation ist in dieser Umgebung weiterhin durch die Runtime blockiert.

Der aktuell dokumentierte Fehler lautet:

```text
Cannot start HTTP server on http://localhost:5000/: Das Handle ist ungueltig. (code 6)
```

Wichtig:

- das Problem liegt unterhalb von `_cfs_web`
- die Direct-Handle-Smokes des Frameworks sind davon nicht betroffen
- sobald die Runtime den Listener stabil starten kann, sollte `run_example_app.cfs` der erste Re-Check sein

## 23. Referenz nach Datei

Wenn du gezielt in den Code springen willst:

- `server.cfs`: Routing, Sessions, Request/Response, Static, Error Pages, Run-Loop
- `middleware.cfs`: Logger, Counter, Header, Security, Rate Limit, Host/Origin Policies
- `forms.cfs`: HTML-Form-Helfer, Error Summary, Flash/Message-Bausteine, Old-Input-Binding
- `validation.cfs`: BoundForm und Feldregeln
- `guards.cfs`: Auth- und Session-Guards
- `cookies.cfs`: Cookie-Parsing, Serialisierung und Signing
- `session_store.cfs`: Default-Store und Store-Contract
- `common.cfs`: Upload-Parsing, Query/Cookie/Date/Host/Origin-Helfer, Content-Types
- `dom.cfs`, `layout.cfs`, `components.cfs`, `views.cfs`: HTML- und View-Schicht
- `component_state.cfs`: sessionbasierter UI-Komponenten-State
- `example_app.cfs`: realistischere Komplett-App als Vorlage
