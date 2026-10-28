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

        if (dict.TryGetValue("Theme", out var theme) && !string.IsNullOrWhiteSpace(theme))
            settings.Theme = theme;

        if (dict.TryGetValue("EnableClipboardMonitoring", out var clip) && bool.TryParse(clip, out var ecm))
            settings.EnableClipboardMonitoring = ecm;

        if (dict.TryGetValue("EnableNotifications", out var notif) && bool.TryParse(notif, out var en))
            settings.EnableNotifications = en;

        if (dict.TryGetValue("InterceptionMode", out var mode) && !string.IsNullOrWhiteSpace(mode))
            settings.InterceptionMode = mode;

        if (dict.TryGetValue("QueueScheduleEnabled", out var schedEn) && bool.TryParse(schedEn, out var qse))
            settings.QueueScheduleEnabled = qse;

        if (dict.TryGetValue("QueueScheduleStartTime", out var schedStart))
            settings.QueueScheduleStartTime = schedStart;

        if (dict.TryGetValue("QueueScheduleStopTime", out var schedStop))
            settings.QueueScheduleStopTime = schedStop;

        if (dict.TryGetValue("ConfirmFileDeletion", out var confDel) && bool.TryParse(confDel, out var cfd))
            settings.ConfirmFileDeletion = cfd;

        if (dict.TryGetValue("DuplicateFileAction", out var dupAct) && !string.IsNullOrWhiteSpace(dupAct))
            settings.DuplicateFileAction = dupAct;

        if (dict.TryGetValue("ViewMode", out var vm) && !string.IsNullOrWhiteSpace(vm))
            settings.ViewMode = vm;

        if (dict.TryGetValue("ShowDetailsPanel", out var sdp) && bool.TryParse(sdp, out var sdpVal))
            settings.ShowDetailsPanel = sdpVal;

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
            ["AllowLocalIpAddresses"] = settings.AllowLocalIpAddresses.ToString(),
            ["Theme"] = settings.Theme,
            ["EnableClipboardMonitoring"] = settings.EnableClipboardMonitoring.ToString(),
            ["EnableNotifications"] = settings.EnableNotifications.ToString(),
            ["InterceptionMode"] = settings.InterceptionMode,
            ["QueueScheduleEnabled"] = settings.QueueScheduleEnabled.ToString(),
            ["QueueScheduleStartTime"] = settings.QueueScheduleStartTime ?? string.Empty,
            ["QueueScheduleStopTime"] = settings.QueueScheduleStopTime ?? string.Empty,
            ["ConfirmFileDeletion"] = settings.ConfirmFileDeletion.ToString(),
            ["DuplicateFileAction"] = settings.DuplicateFileAction,
            ["ViewMode"] = settings.ViewMode,
            ["ShowDetailsPanel"] = settings.ShowDetailsPanel.ToString()
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
