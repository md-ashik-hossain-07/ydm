using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace YDM.Storage.Migrations;

public class MigrationService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public MigrationService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ApplyMigrationsAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        int currentVersion = 0;
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.Transaction = transaction;
            versionCmd.CommandText = "PRAGMA user_version;";
            var result = await versionCmd.ExecuteScalarAsync();
            if (result != null && result != System.DBNull.Value)
            {
                currentVersion = System.Convert.ToInt32(result);
            }
        }

        if (currentVersion < 1)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Downloads (
                        Id TEXT PRIMARY KEY,
                        Url TEXT NOT NULL,
                        Referrer TEXT,
                        Cookies TEXT,
                        UserAgent TEXT,
                        FileName TEXT NOT NULL,
                        DestinationPath TEXT NOT NULL,
                        TempDirectory TEXT NOT NULL,
                        TotalBytes INTEGER,
                        DownloadedBytes INTEGER NOT NULL DEFAULT 0,
                        IsResumable INTEGER NOT NULL DEFAULT 0,
                        ETag TEXT,
                        LastModified TEXT,
                        Status INTEGER NOT NULL DEFAULT 0,
                        Priority INTEGER NOT NULL DEFAULT 1,
                        MaxConnections INTEGER NOT NULL DEFAULT 8,
                        CreatedAt TEXT NOT NULL,
                        CompletedAt TEXT,
                        ScheduledAt TEXT,
                        ErrorMessage TEXT
                    );

                    CREATE INDEX IF NOT EXISTS IX_Downloads_Status ON Downloads(Status);
                    CREATE INDEX IF NOT EXISTS IX_Downloads_CreatedAt ON Downloads(CreatedAt);

                    CREATE TABLE IF NOT EXISTS Segments (
                        Id TEXT PRIMARY KEY,
                        JobId TEXT NOT NULL,
                        SegmentIndex INTEGER NOT NULL,
                        StartOffset INTEGER NOT NULL,
                        EndOffset INTEGER NOT NULL,
                        CurrentOffset INTEGER NOT NULL,
                        Status INTEGER NOT NULL DEFAULT 0,
                        TempFilePath TEXT NOT NULL,
                        FOREIGN KEY(JobId) REFERENCES Downloads(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IX_Segments_JobId ON Segments(JobId);

                    CREATE TABLE IF NOT EXISTS Settings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT NOT NULL
                    );

                    PRAGMA user_version = 2;
                ";
                await cmd.ExecuteNonQueryAsync();
            }
        }
        else if (currentVersion < 2)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    ALTER TABLE Downloads ADD COLUMN ScheduledAt TEXT;
                    PRAGMA user_version = 2;
                ";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }
}
