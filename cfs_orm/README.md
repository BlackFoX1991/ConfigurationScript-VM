# CFS ORM

Erstes ORM-Grundgeruest fuer die Configuration Language als reine CFS-Bibliothek auf Basis des bestehenden SQL-Plugins.

## Ziel

Dieses Paket ist bewusst kein "magisches" Reflection-ORM. Das Mapping wird explizit in CFS beschrieben. Dadurch passt es zu deiner aktuellen Laufzeit, ohne dass die VM zuerst Klassen-Reflection auf CFS-Objekte bekommen muss.

## Dateien

- `core.cfs`: kleine Hilfsfunktionen fuer Dictionaries, Arrays, SQL-Identifier und Tabellen-Namen
- `mapping.cfs`: Mapping-DSL, Spaltenspezifikation und Relationsdefinitionen
- `query.cfs`: SQL-Erzeugung fuer `SELECT`, `INSERT`, `UPDATE`, `DELETE`
- `relations.cfs`: Eager-Loading fuer `belongs_to`, `has_one`, `has_many`
- `builder.cfs`: kleiner fluenter Query-Builder ueber der Plan-Schicht
- `unit_of_work.cfs`: einfache Change-Tracking- und Flush-Schicht
- `lazy.cfs`: explizites Lazy Loading ueber angehaengte Loader-Funktionen
- `schema.cfs`: SQL-Erzeugung und Schema-Inspektion fuer SQL Server
- `migrations.cfs`: einfacher Migrations-Runner mit History-Tabelle
- `repository.cfs`: `Session` und `Repository` als bequeme ORM-Schicht
- `example_sqlserver.cfs`: Beispiel fuer SQL Server mit dem vorhandenen Plugin
- `migration_example.cfs`: Beispiel fuer Schema-Erzeugung und Migrationen

## Kernidee

Ein Mapping besteht aus:

- Tabellenname
- Schema
- Spaltendefinitionen
- optionalem Primaerschluessel
- optionaler `factory(row)` fuer echte CFS-Klassen
- optionalem `to_row(entity)` fuer Klasseninstanzen beim Schreiben
- optionalen `relations`

Minimal mit Dictionary-Entities:

```cfs
import { entity, column, has_many } from "mapping.cfs";
import { Session } from "repository.cfs";

var PostMap = entity("Posts", {
    "id": column("Id", key: true, generated: true, insertable: false, updatable: false),
    "user_id": column("UserId"),
    "title": column("Title")
});

var UserMap = entity(
    "Users",
    {
        "id": column("Id", key: true, generated: true, insertable: false, updatable: false),
        "name": column("Name"),
        "email": column("Email")
    },
    key: "id",
    key_generated: true,
    relations: {
        "posts": has_many(PostMap, "user_id", "id", "Id ASC")
    }
);
```

Danach:

```cfs
var session = new Session(conn);
var users = session.repo(UserMap);

var active = await users.builder()
    .where("[IsActive] = @active", {"active": 1})
    .order_by("Id DESC")
    .top(10)
    .all_async();

await users.include_async(active, "posts");
```

Explizites Lazy Loading:

```cfs
var user = await users.first_async(42);
users.attach_lazy([user], "posts");

var posts = await user["load_posts"]();
print(user["posts_loaded"]);
```

Aggregationen ueber den Builder:

```cfs
var total = await users.builder()
    .where("[IsActive] = @active", {"active": 1})
    .count_async();

var any = await users.builder()
    .where("[Email] = @mail", {"mail": "ada@example.com"})
    .exists_async();

var max_id = await users.builder()
    .where("[IsActive] = @active", {"active": 1})
    .max_async("id");
```

Einfache Unit of Work:

```cfs
var uow = session.unit_of_work();
var user = await users.first_async(42);

uow.watch(UserMap, user);
user.email = "ada@new.example";

print(uow.is_dirty(user));
print(uow.dirty_properties(user));

uow.register_new(UserMap, {
    "name": "Grace",
    "email": "grace@example.com"
});

await uow.flush_async();
```

Schema und Migrationen:

```cfs
import { create_table_sql } from "schema.cfs";
import { migration, Migrator, schema_diff_migration } from "migrations.cfs";

var migrator = new Migrator(session);

await migrator.apply_all_async([
    migration("001_create_users", create_table_sql(UserMap)),
    schema_diff_migration("002_sync_users", UserMap, true, false)
]);
```

Konservatives Schema Sync:

```cfs
import { sync_table_async } from "schema.cfs";

var result = await sync_table_async(session, UserMap);
print(result["created"]);
print(len(result["added"]));
```

Diff-basierte Schema-Anpassung:

```cfs
import { apply_table_diff_async } from "schema.cfs";

var diff = await apply_table_diff_async(session, UserMap, true, true);
print(len(diff["alter"]));
print(len(diff["drop"]));
```

Gruppierte Abfragen:

```cfs
var rows = await posts.builder()
    .group_by("user_id")
    .having("COUNT_BIG(1) >= @minPosts", {"minPosts": 2})
    .order_by("[user_id] ASC")
    .group_count_async();
```

## Aktueller Stand

- Fokus ist SQL Server, passend zum vorhandenen `CFGS.Microsoft.SQL.dll`
- `SELECT`, `SELECT BY KEY`, `INSERT`, `INSERT + SCOPE_IDENTITY`, `UPDATE`, `DELETE` sind enthalten
- Builder fuer `where`, `and_where`, `order_by`, `top`, `count`, `exists`, `sum`, `min`, `max`, `avg`
- Eager-Loading fuer `belongs_to`, `has_one`, `has_many`
- explizites Lazy Loading ueber `attach_lazy(...)` und `load_<relation>()`
- `UnitOfWork` fuer `watch`, `register_new`, `register_deleted`, `flush`
- tieferes Dirty-Tracking ueber `is_dirty`, `dirty_properties`, `dirty_entries`
- Schema-Toolkit fuer `create_table_sql`, `add_column_sql`, `ensure_table`, `table_exists`, `column_exists`, `sync_table`
- Migrations-Runner mit `Migrator` und History-Tabelle `dbo.__CfsMigrations`
- diff-basierte Schema-Anpassung ueber `diff_table`, `apply_table_diff` und `schema_diff_migration`
- gruppierte Abfragen ueber `group_by`, `having`, `group_count`, `group_sum`, `group_min`, `group_max`, `group_avg`
- fuer Klasseninstanzen ist `to_row(entity)` noetig
- fuer objektorientierte Rueckgabe ist `factory(row)` noetig

## Naechste sinnvolle Ausbaustufen

- detaillierteres Dirty-Tracking fuer Arrays und verschachtelte Strukturen
- raw SQL fragments fuer komplexere Aggregationen und gruppierte Abfragen
- destruktive oder diff-basierte Schema-Migrationen mit Typaenderungen
