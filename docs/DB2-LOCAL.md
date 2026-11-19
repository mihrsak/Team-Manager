# Db2 LUW local runtime

The relational workspace supports Db2 LUW 12.1.5 with Oracle compatibility enabled and the official IBM .NET 10 driver (`10.0.0.400`). It uses the same authorization and transaction domain as Oracle. Provider details live in `Data/RelationalClient.cs`; active modules use targeted SQL and scoped transactional locks. This does not establish support for Db2 z/OS or production capacity.

## Database and configuration

The local test instance uses IBM Community image `icr.io/db2_community/db2:latest`, database `TEAMMGR`, UTF-8, 32 KiB pages, and loopback port `7588`. Image setup uses `LICENSE=accept`, `ENABLE_ORACLE_COMPATIBILITY=true` and a generated instance password stored outside Git. A rootful privileged Podman container was needed: rootless instance initialization failed on the machine's `nosuid` mount. The test container has a named volume, a 3 GiB memory limit and 1 GiB shared memory. Database manager `INSTANCE_MEMORY` is 524288 pages. These are local fixture settings, not a production sizing decision.

Configure the published application through an ignored `appsettings.Local.json` or environment variables:

```json
{
  "Database": { "Provider": "Db2" },
  "ConnectionStrings": {
    "TeamManagerDb2": "Database=TEAMMGR;Server=localhost:7588;UID=<user>;PWD=<secret>;CurrentSchema=<schema>;"
  }
}
```

Use a dedicated schema and supply credentials locally. `CurrentSchema` scopes tables, sequence discovery and application locks; see [IBM CurrentSchema documentation](https://www.ibm.com/docs/en/db2/11.5.x?topic=keywords-currentschema). The runtime never falls back to SQL Server. Keep certificates, credentials, snapshots and attachments outside source control.

## Oracle-to-Db2 migration

Stop application writers and back up the source before exporting. In the Oracle-configured published directory:

```powershell
dotnet ./TeamManager.Web.dll --export-relational ../oracle-snapshot.json
```

The snapshot includes private data and password hashes. Transfer it securely to the Db2-configured package, then run:

```powershell
dotnet ./TeamManager.Web.dll --import-db2-relational ../oracle-snapshot.json
```

The importer creates 51 typed tables, restores rows and sequence high-water marks, validates foreign keys, independently compares all records, freezes the archival JSON and installs runtime protocol 5. It refuses an already installed workspace or nonempty target tables. Data import and validation occur in one transaction; base schema creation is resumable. If data import committed but final concurrency installation failed, resume the idempotent installer:

```powershell
dotnet ./TeamManager.Web.dll --apply-relational-concurrency
```

Readiness stays unhealthy until schema 10 and lock protocol 5 are installed. Db2 checks foreign keys immediately, so deletes run child-first and inserts parent-first. Nullable unique keys use null-excluding unique indexes; referenced `CustomFields.Scope` is non-null. Import fails rather than dropping data that violates these rules. Oracle's original deferred constraints remain in place.

## Verification

Run the dedicated test against a loopback database and a private Oracle snapshot:

```powershell
dotnet run --project scripts/db2-relational-tests -- <ignored-db2-settings.json> <oracle-snapshot.json>
```

It creates a fresh `TMR_QA_<12 hex>` schema and retains it for inspection. The test checks the full migration, frozen archive, installer retry, Unicode and empty text, concurrent IDs, stale revisions, group revocation with queued writers, independent writes, real loopback SMTP, HR/EN messages, retry, reminder and lease ownership. An optional third argument reuses only a schema matching that strict generated name for runtime test iteration; omit it to prove fresh migration.

Browser suites accept `TEAM_MANAGER_ORACLE_URL` (module suite) or `TEAM_MANAGER_QA_URL` (general suite), set to the Db2 application URL. The existing test names reflect their original Oracle target; behavior runs against the selected URL. Set `TEAM_MANAGER_REPOSITORY_FIXTURE_OUT` to the ignored local Git fixture file to include commit/diff views in the 52 HR/EN accessibility scans. Mail-gateway tests read their secret from `TEAM_MANAGER_MAIL_SETTINGS` without printing it.

The Linux image includes `libxml2` and sets `LD_LIBRARY_PATH=/app/clidriver/lib` for IBM's native client. The local application container `team-manager-db2-web-qa` runs as UID 1654, uses a dedicated named volume for Data Protection keys, and connects through an ignored environment file. `scripts/Test-Db2Container.cjs` verifies its HTTPS proxy on port 7591, secure authentication cookie and retained session after restarting that exact local test container. It requires the existing Edge browser and local proxy fixture. The Windows demo remains on port 7589; the earlier Oracle demo remains on 7572.

The IBM driver inherits blocking implementations of several async APIs. The adapter runs those calls on the worker pool and forwards cancellation to the native command; transaction locks still live until commit/rollback. Production concurrency and pool sizing require measurement on the target environment.
