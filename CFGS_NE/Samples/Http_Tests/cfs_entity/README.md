# CFS Entity

`cfs_entity` is a small ORM-style layer for CFGS scripts that use the official
MS SQL plugin. It is written in CFGS and follows the same idea as Entity
Framework at script scale: define a model, open a context, use an entity set, track
changes, and save them in one transaction.

## Import

```cfs
import "cfs_entity/entity.cfs";

use Cfs.Entity;
```

`entity.cfs` imports `../dist/Debug/net10.0/CFGS.Microsoft.SQL.dll` so it works
from the repository checkout after a normal build. For packaged applications,
adjust that import path to your plugin layout.

## Define A Model

```cfs
func user_model() {
    return new EntityModel("dbo", "Users")
        .key("Id", "Id", "INT", true)
        .required("Name", "Name", "NVARCHAR(120)")
        .required("Email", "Email", "NVARCHAR(256)")
        .column("IsActive", "IsActive", "BIT", false, "1");
}
```

The first argument is the CFGS property name, the second argument is the SQL
column name. Entities are normal CFGS dictionaries, so a row looks like this:

```cfs
{
    "Id": 1,
    "Name": "Ada Lovelace",
    "Email": "ada@example.test",
    "IsActive": true
}
```

## Context And Sets

```cfs
var db = new DbContext(connection_string);
await db.open();

var users_model = user_model();
await db.migrate_async([users_model]);

var users = db.entity_set(users_model);
```

`migrate_async(models)` now uses the migration-history table and automatic
model diffing for safe additive changes.

```cfs
await db.migrate_async([users_model]);
```

## Insert, Update, Delete

```cfs
var entry = users.add({
    "Name": "Ada Lovelace",
    "Email": "ada@example.test",
    "IsActive": true
});

await db.save_changes_async();

entry.set_value("Email", "ada.lovelace@example.test");
await db.save_changes_async();

users.remove_entity(entry.data);
await db.save_changes_async();
```

`save_changes_async()` wraps pending changes in a SQL transaction. Identity keys
are written back into the entry data after insert.

## Querying

```cfs
var ada = await users.find_async(1);

var active_users = await users.query()
    .where_eq("IsActive", true)
    .order_by("Id", SortDirection["Desc"])
    .top(20)
    .to_list_async();

var count = await users.query()
    .where_like("Email", "%@example.test")
    .count_async();
```

For advanced filters, use `where_sql` with parameters.

```cfs
var rows = await users.query()
    .where_sql("[Name] LIKE @name", {"name": "Ada%"})
    .to_list_async();
```

## Included API

- `EntityModel`: schema, table, columns, keys, DDL generation.
- `DbContext`: connection lifecycle, sets, change tracking, migrations,
  transactions, `save_changes_async`.
- `DbSet`: `add`, `update`, `remove_entity`, `find_async`, `all_async`, direct insert,
  update and delete helpers.
- `EntityQuery`: fluent query builder with `where_eq`, `where_like`,
  `where_sql`, `order_by`, `top`, `select`, `to_list_async`,
  `first_or_null_async`, and `count_async`.
- `EntityEntry`: tracked data plus `get`, `set_value`, and `mark_deleted`.
- `EntityState`: enum for `Unchanged`, `Added`, `Modified`, and `Deleted`.
- `SortDirection`: enum for `Asc` and `Desc`.
- `Migration`: manual migration object with `up`, `down`, snapshot and checksum.
- `MigrationRunner`: applies pending migrations, records history, rolls back
  the latest registered migration, and creates model-diff plans.
- `ModelSnapshot`: converts model definitions into serializable snapshots.
- `ModelDiffer`: compares snapshots and creates SQL plans for additive changes,
  table rebuilds, and archive-based removals.
- `DatabaseFacade`: available as `db.database` for migration workflows.

This is intentionally parameterized by default. Raw SQL is only used where the
caller explicitly provides a filter through `where_sql`.

## Manual Migrations

Manual migrations are the safest way to handle schema creation, data fixes,
renames, type changes, and destructive operations.

```cfs
import "cfs_entity/entity.cfs";
use Cfs.Entity;

func user_model() {
    return new EntityModel("dbo", "Users")
        .key("Id", "Id", "INT", true)
        .required("Name", "Name", "NVARCHAR(120)");
}

var create_users = new Migration(
    "202606160001_CreateUsers",
    "Create Users",
    async func(db) {
        await db.conn.execute_nonquery_async(user_model().create_table_sql());
        return true;
    },
    async func(db) {
        await db.conn.execute_nonquery_async("DROP TABLE IF EXISTS [dbo].[Users]");
        return true;
    },
    ModelSnapshot.from_models([user_model()])
);

var db = new DbContext(connection_string);
await db.database.migrate_async([create_users]);
```

The runner creates and uses:

```sql
dbo.__CfgsEntityMigrations
```

Stored columns are `Id`, `Name`, `Checksum`, `AppliedAtUtc`, and `Snapshot`.

## Automatic Model Diff

For additive model changes, the database facade can create and apply a plan from
the latest stored snapshot.

```cfs
await db.database.update_schema_async(
    "202606160002_AddUserLastLogin",
    "Add user last-login column",
    [user_model_v2()]
);
```

The automatic differ executes safe operations:

- create table
- add column
- archive removed table
- rebuild table for removed columns
- rebuild table for type, nullability, default, key, or identity changes

Risky table changes use this strategy:

- create a temporary table with the new shape
- copy all still-existing columns from the old table
- keep identity values when possible
- rename the old table to a `__CfgsEntityArchive_*` table
- rename the temporary table to the original table name

This preserves the old data in an archive table instead of deleting it. If type
conversion fails while copying, SQL Server rolls the migration transaction back.

The automatic differ still blocks one case:

- adding a new `NOT NULL` column without a default value

Use a manual `Migration` for that case so the missing value can be filled
explicitly.

## Local Selftest

The selftest validates model metadata, enum usage, query SQL generation, and
change tracking without opening a database connection.

```powershell
dotnet dist\Debug\net10.0\CFGS_VM.dll cfs_entity\selftest.cfs
```
