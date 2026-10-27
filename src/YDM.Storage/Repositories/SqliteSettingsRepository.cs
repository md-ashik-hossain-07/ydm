using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Storage.Migrations;

namespace YDM.Storage.Repositories;

public class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync()
    {
        var migrationService = new MigrationService(_connectionFactory);
        await migrationService.ApplyMigrationsAsync();
    }

    public async Task<ApplicationSettings> GetSettingsAsync()
    {
        var settings = new ApplicationSettings();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string key = reader.GetString(0);
            string value = reader.GetString(1);
            dict[key] = value;
        }

        if (dict.TryGetValue("DefaultDownloadDirectory", out var dir) && !string.IsNullOrWhiteSpace(dir))
            settings.DefaultDownloadDirectory = dir;

        if (dict.TryGetValue("MaxConcurrentDownloads", out var maxConcurrent) && int.TryParse(maxConcurrent, out var mc))
            settings.MaxConcurrentDownloads = Math.Clamp(mc, 1, 10);

        if (dict.TryGetValue("DefaultConnectionsPerDownload", out var connPerDown) && int.TryParse(connPerDown, out var cpd))
            settings.DefaultConnectionsPerDownload = Math.Clamp(cpd, 1, 32);

        if (dict.TryGetValue("AutoStartDownloads", out var autoStart) && bool.TryParse(autoStart, out var asd))
            settings.AutoStartDownloads = asd;

        if (dict.TryGetValue("EnableBrowserInterception", out var interception) && bool.TryParse(interception, out var ebi))
            settings.EnableBrowserInterception = ebi;

        if (dict.TryGetValue("SpeedLimitBytesPerSecond", out var speedLimit) && int.TryParse(speedLimit, out var sl))
            settings.SpeedLimitBytesPerSecond = Math.Max(0, sl);

        if (dict.TryGetValue("NetworkTimeoutSeconds", out var timeout) && int.TryParse(timeout, out var to))
            settings.NetworkTimeoutSeconds = Math.Clamp(to, 5, 300);

        if (dict.TryGetValue("AllowLocalIpAddresses", out var allowLocal) && bool.TryParse(allowLocal, out var ala))
            settings.AllowLocalIpAddresses = ala;

        return settings;
    }

    public async Task SaveSettingsAsync(ApplicationSettings settings)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        var dict = new Dictionary<string, string>
        {
            ["DefaultDownloadDirectory"] = settings.DefaultDownloadDirectory,
            ["MaxConcurrentDownloads"] = settings.MaxConcurrentDownloads.ToString(),
            ["DefaultConnectionsPerDownload"] = settings.DefaultConnectionsPerDownload.ToString(),
            ["AutoStartDownloads"] = settings.AutoStartDownloads.ToString(),
            ["EnableBrowserInterception"] = settings.EnableBrowserInterception.ToString(),
            ["SpeedLimitBytesPerSecond"] = settings.SpeedLimitBytesPerSecond.ToString(),
            ["NetworkTimeoutSeconds"] = settings.NetworkTimeoutSeconds.ToString(),
            ["AllowLocalIpAddresses"] = settings.AllowLocalIpAddresses.ToString()
        };

        foreach (var kvp in dict)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Settings (Key, Value) 
                VALUES (@Key, @Value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            ";
            cmd.Parameters.AddWithValue("@Key", kvp.Key);
            cmd.Parameters.AddWithValue("@Value", kvp.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }
}
