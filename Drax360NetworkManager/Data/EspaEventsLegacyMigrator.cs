using System;
using Microsoft.Data.Sqlite;

namespace DraxTechnology.Data
{
    // One-shot raw-SQL migration for the ESPA Events database. Runs at panel
    // startup before EF Core takes over. Idempotent: if the schema is already
    // at the target version (or the database doesn't exist yet), it's a no-op.
    //
    // Why a separate raw-SQL pass instead of EF Core migrations: existing
    // deployments may have a legacy v0 schema where Node/Loop/Device were
    // declared without explicit INTEGER types (the original DDL used SQLite's
    // affinity inference). EF.EnsureCreated() would see the table and do
    // nothing, leaving the columns as TEXT-affinity. We need to rebuild the
    // table as proper INTEGER NOT NULL before EF picks up.
    public static class EspaEventsLegacyMigrator
    {
        public const int TargetVersion = 1;

        private const string CreateDdl =
            "CREATE TABLE [Events] (" +
            "    [Id]     INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "    [Node]   INTEGER NOT NULL, " +
            "    [Loop]   INTEGER NOT NULL, " +
            "    [Device] INTEGER NOT NULL, " +
            "    [Name]   TEXT    NOT NULL UNIQUE" +
            ");";

        public static void EnsureMigrated(string dbPath, Action<string> log = null)
        {
            using var connection = new SqliteConnection("Data Source=" + dbPath);
            connection.Open();

            int currentVersion = ExecScalarInt(connection, "PRAGMA user_version;");
            bool tableExists = ExecScalarInt(connection,
                "SELECT count(*) FROM [sqlite_master] " +
                "WHERE [type] = 'table' AND [name] = 'Events';") > 0;

            log?.Invoke(
                "ESPA Events DB: schema check (current v" + currentVersion +
                ", target v" + TargetVersion +
                ", tableExists=" + tableExists + ")");

            if (!tableExists)
            {
                // Fresh install: leave it. EF.EnsureCreated() will build the
                // table from the entity model. We still bump user_version
                // so the next start short-circuits the legacy check.
                SetUserVersion(connection);
                log?.Invoke("ESPA Events DB: no table yet — EF will create");
                return;
            }

            if (currentVersion >= TargetVersion)
            {
                log?.Invoke("ESPA Events DB: already at v" + currentVersion + ", no migration");
                return;
            }

            log?.Invoke("ESPA Events DB: migrating v" + currentVersion + " -> v" + TargetVersion);

            int legacyCount = ExecScalarInt(connection, "SELECT count(*) FROM [Events];");
            int nullNameCount = ExecScalarInt(connection,
                "SELECT count(*) FROM [Events] WHERE [Name] IS NULL;");

            // Rebuild via rename + copy with CAST. Rows missing a Name (which
            // would violate NOT NULL) are dropped — the legacy schema permitted
            // them in theory.
            using (var tx = connection.BeginTransaction())
            {
                Exec(connection, tx, "ALTER TABLE [Events] RENAME TO [Events_legacy];");
                Exec(connection, tx, CreateDdl);
                Exec(connection, tx,
                    "INSERT INTO [Events] ([Id], [Node], [Loop], [Device], [Name]) " +
                    "SELECT [Id], " +
                    "       CAST([Node]   AS INTEGER), " +
                    "       CAST([Loop]   AS INTEGER), " +
                    "       CAST([Device] AS INTEGER), " +
                    "       [Name] " +
                    "FROM [Events_legacy] " +
                    "WHERE [Name] IS NOT NULL;");
                Exec(connection, tx, "DROP TABLE [Events_legacy];");
                tx.Commit();
            }

            int kept = ExecScalarInt(connection, "SELECT count(*) FROM [Events];");
            log?.Invoke(
                "ESPA Events DB: migration complete (" +
                kept + " kept, " + nullNameCount + " dropped, " +
                (legacyCount - kept - nullNameCount) + " other-skipped)");

            SetUserVersion(connection);
        }

        private static void SetUserVersion(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            // PRAGMA does not accept parameters, but TargetVersion is a
            // compile-time constant so there's no injection surface.
            cmd.CommandText = "PRAGMA user_version = " + TargetVersion + ";";
            cmd.ExecuteNonQuery();
        }

        private static void Exec(SqliteConnection connection, SqliteTransaction tx, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static int ExecScalarInt(SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            object result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }
    }
}
