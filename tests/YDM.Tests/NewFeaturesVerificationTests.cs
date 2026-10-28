using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YDM.Core;
using YDM.Core.Models;
using YDM.Core.Security;
using YDM.Engine;
using YDM.Queue;
using YDM.Storage;
using YDM.Storage.Migrations;
using YDM.Storage.Repositories;

namespace YDM.Tests;

public class NewFeaturesVerificationTests
{
    [Fact]
    public void DestinationValidation_WritableDirectory_ReturnsValid()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_val_{Guid.NewGuid():N}");
        try
        {
            var result = PathSanitizer.ValidateDestinationDirectory(tempDir, requiredBytes: 1024);
            Assert.True(result.IsValid);
            Assert.Null(result.ErrorMessage);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void DestinationValidation_NonExistentDrive_ReturnsError()
    {
        // Drive Z:\ or Q:\ is virtually always non-existent on typical Windows workstations
        string nonExistentPath = @"Z:\NonExistentFolder\Downloads";
        if (!Directory.Exists(@"Z:\"))
        {
            var result = PathSanitizer.ValidateDestinationDirectory(nonExistentPath);
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("not accessible", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DestinationValidation_EmptyPath_ReturnsError()
    {
        var result = PathSanitizer.ValidateDestinationDirectory("");
        Assert.False(result.IsValid);
        Assert.Contains("cannot be empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DestinationValidation_HugeDiskSpace_RejectsWhenExceedsDriveCapacity()
    {
        string tempDir = Path.GetTempPath();
        // Request 100 Petabytes
        long ridiculousSize = 100L * 1024 * 1024 * 1024 * 1024 * 1024;
        var result = PathSanitizer.ValidateDestinationDirectory(tempDir, requiredBytes: ridiculousSize);
        Assert.False(result.IsValid);
        Assert.Contains("Insufficient disk space", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDestinationPath_AutoRename_CreatesNumberedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string file1 = Path.Combine(tempDir, "document.pdf");
            File.WriteAllText(file1, "sample 1");

            string target = PathSanitizer.ResolveDestinationPath(tempDir, "document.pdf", DuplicateResolution.AutoRename);
            Assert.Equal(Path.Combine(tempDir, "document (1).pdf"), target);

            File.WriteAllText(target, "sample 2");
            string target2 = PathSanitizer.ResolveDestinationPath(tempDir, "document.pdf", DuplicateResolution.AutoRename);
            Assert.Equal(Path.Combine(tempDir, "document (2).pdf"), target2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void ResolveDestinationPath_Overwrite_ReturnsExactExistingPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_dup_ov_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string file1 = Path.Combine(tempDir, "document.pdf");
            File.WriteAllText(file1, "sample");

            string target = PathSanitizer.ResolveDestinationPath(tempDir, "document.pdf", DuplicateResolution.Overwrite);
            Assert.Equal(file1, target);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task SettingsRepository_PersistsNewSettings_IncludingThemeAndScheduler()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_set_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "settings.db");

        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteSettingsRepository(factory);
        await repo.InitializeAsync();

        try
        {
            var settings = new ApplicationSettings
            {
                DefaultDownloadDirectory = @"D:\MyCustomDownloads",
                MaxConcurrentDownloads = 5,
                DefaultConnectionsPerDownload = 16,
                Theme = "Dark",
                EnableClipboardMonitoring = true,
                EnableNotifications = false,
                InterceptionMode = "Ask",
                QueueScheduleEnabled = true,
                QueueScheduleStartTime = "03:30",
                QueueScheduleStopTime = "07:00",
                ConfirmFileDeletion = false,
                DuplicateFileAction = "Overwrite"
            };

            await repo.SaveSettingsAsync(settings);

            var loaded = await repo.GetSettingsAsync();
            Assert.Equal(@"D:\MyCustomDownloads", loaded.DefaultDownloadDirectory);
            Assert.Equal(5, loaded.MaxConcurrentDownloads);
            Assert.Equal(16, loaded.DefaultConnectionsPerDownload);
            Assert.Equal("Dark", loaded.Theme);
            Assert.True(loaded.EnableClipboardMonitoring);
            Assert.False(loaded.EnableNotifications);
            Assert.Equal("Ask", loaded.InterceptionMode);
            Assert.True(loaded.QueueScheduleEnabled);
            Assert.Equal("03:30", loaded.QueueScheduleStartTime);
            Assert.Equal("07:00", loaded.QueueScheduleStopTime);
            Assert.False(loaded.ConfirmFileDeletion);
            Assert.Equal("Overwrite", loaded.DuplicateFileAction);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task DatabaseMigration_UpgradesVersion1ToVersion2_PreservingHistoryAndAddingScheduledAt()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_mig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "mig_test.db");

        var factory = new SqliteConnectionFactory(dbPath);

        // Step 1: Create a legacy v1 database
        using (var conn = factory.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE Downloads (
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
                    ErrorMessage TEXT
                );
                CREATE TABLE Segments (
                    Id TEXT PRIMARY KEY,
                    JobId TEXT NOT NULL,
                    SegmentIndex INTEGER NOT NULL,
                    StartOffset INTEGER NOT NULL,
                    EndOffset INTEGER NOT NULL,
                    CurrentOffset INTEGER NOT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    TempFilePath TEXT NOT NULL
                );
                CREATE TABLE Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                PRAGMA user_version = 1;

                INSERT INTO Downloads (Id, Url, FileName, DestinationPath, TempDirectory, CreatedAt)
                VALUES ('11111111-1111-1111-1111-111111111111', 'https://example.com/file.zip', 'file.zip', 'C:\Downloads\file.zip', 'C:\Temp\1', '2026-10-28T00:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Step 2: Run MigrationService to apply v2 migrations
        var migrationService = new MigrationService(factory);
        await migrationService.ApplyMigrationsAsync();

        // Step 3: Verify existing data preserved and new column present
        var repo = new SqliteDownloadRepository(factory);
        var existingJob = await repo.GetByIdAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.NotNull(existingJob);
        Assert.Equal("file.zip", existingJob.FileName);
        Assert.Null(existingJob.ScheduledAt);

        // Step 4: Save new job with ScheduledAt
        var newJob = new DownloadJob
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/scheduled.iso",
            FileName = "scheduled.iso",
            DestinationPath = Path.Combine(tempDir, "scheduled.iso"),
            TempDirectory = Path.Combine(tempDir, "temp"),
            ScheduledAt = DateTime.UtcNow.AddHours(2)
        };
        await repo.SaveJobAsync(newJob);

        var retrieved = await repo.GetByIdAsync(newJob.Id);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.ScheduledAt);
        Assert.Equal(newJob.ScheduledAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm"), retrieved.ScheduledAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm"));

        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadQueueManager_RecoversInterruptedJobs_ToQueuedStatus()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ydm_q_rec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string dbPath = Path.Combine(tempDir, "queue.db");

        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(factory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);
        var queueManager = new DownloadQueueManager(engine, repo, maxConcurrency: 2);

        try
        {
            var job1 = new DownloadJob
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file1.zip",
                FileName = "file1.zip",
                DestinationPath = Path.Combine(tempDir, "file1.zip"),
                TempDirectory = Path.Combine(tempDir, "temp1"),
                Status = JobStatus.Downloading
            };
            var job2 = new DownloadJob
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file2.zip",
                FileName = "file2.zip",
                DestinationPath = Path.Combine(tempDir, "file2.zip"),
                TempDirectory = Path.Combine(tempDir, "temp2"),
                Status = JobStatus.Assembling
            };
            var job3 = new DownloadJob
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file3.zip",
                FileName = "file3.zip",
                DestinationPath = Path.Combine(tempDir, "file3.zip"),
                TempDirectory = Path.Combine(tempDir, "temp3"),
                Status = JobStatus.Completed
            };

            await repo.SaveJobAsync(job1);
            await repo.SaveJobAsync(job2);
            await repo.SaveJobAsync(job3);

            // Recover
            await queueManager.RecoverInterruptedJobsAsync();

            var rec1 = await repo.GetByIdAsync(job1.Id);
            var rec2 = await repo.GetByIdAsync(job2.Id);
            var rec3 = await repo.GetByIdAsync(job3.Id);

            Assert.Equal(JobStatus.Queued, rec1!.Status);
            Assert.Equal(JobStatus.Queued, rec2!.Status);
            Assert.Equal(JobStatus.Completed, rec3!.Status);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData(13264L, true, 8, 1)] // 13 KB W3C dummy PDF -> 1 connection (preventing unnecessary overhead)
    [InlineData(32768L, true, 8, 1)] // 32 KB -> 1 connection
    [InlineData(65536L, true, 8, 1)] // 64 KB -> 1 connection
    [InlineData(131072L, true, 8, 2)] // 128 KB -> 2 connections
    [InlineData(524288L, true, 8, 8)] // 512 KB -> 8 connections
    [InlineData(104857600L, true, 8, 8)] // 100 MB, max 8 -> 8 connections
    [InlineData(104857600L, true, 16, 16)] // 100 MB, max 16 -> 16 connections
    [InlineData(104857600L, false, 8, 1)] // Non-resumable -> 1 connection always
    [InlineData(null, true, 8, 1)] // Unknown size -> 1 connection
    public void AdaptiveConnectionSizing_CalculatesAccuratelyBasedOnFileSize(object? totalBytesObj, bool isResumable, int maxConnections, int expectedConnections)
    {
        long? totalBytes = totalBytesObj != null ? Convert.ToInt64(totalBytesObj) : null;
        int actual = DownloadEngine.CalculateAdaptiveConnections(totalBytes, isResumable, maxConnections);
        Assert.Equal(expectedConnections, actual);
    }
}