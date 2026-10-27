using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace YDM.Storage;

public class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ydmDir = Path.Combine(appData, "YDM");
            if (!Directory.Exists(ydmDir))
            {
                Directory.CreateDirectory(ydmDir);
            }
            dbPath = Path.Combine(ydmDir, "ydm.db");
        }
        else
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
            ";
            cmd.ExecuteNonQuery();
        }

        return conn;
    }
}
