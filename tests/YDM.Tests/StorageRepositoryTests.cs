using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using YDM.Core;
using YDM.Core.Models;
using YDM.Storage;
using YDM.Storage.Repositories;

namespace YDM.Tests;

public class StorageRepositoryTests
{
    [Fact]
    public async Task SqliteRepository_PersistsAndRecoversStateAcrossRestarts()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"ydm_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory1 = new SqliteConnectionFactory(dbPath);
            var repo1 = new SqliteDownloadRepository(factory1);
            await repo1.InitializeAsync();

            var job = new DownloadJob
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file.zip",
                FileName = "file.zip",
                DestinationPath = @"C:\Downloads\file.zip",
                TempDirectory = @"C:\Temp\file",
                TotalBytes = 1048576,
                DownloadedBytes = 524288,
                IsResumable = true,
                ETag = "\"abc123etag\"",
                Status = JobStatus.Paused,
                MaxConnections = 4
            };

            var seg1 = new DownloadSegment
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Index = 0,
                StartOffset = 0,
                EndOffset = 524287,
                CurrentOffset = 524288,
                Status = SegmentStatus.Completed,
                TempFilePath = @"C:\Temp\file\segment_0.tmp"
            };

            var seg2 = new DownloadSegment
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Index = 1,
                StartOffset = 524288,
                EndOffset = 1048575,
                CurrentOffset = 524288,
                Status = SegmentStatus.Pending,
                TempFilePath = @"C:\Temp\file\segment_1.tmp"
            };

            job.Segments.Add(seg1);
            job.Segments.Add(seg2);

            await repo1.SaveJobAsync(job);

            // Simulate Application Restart: New factory and repo instance pointing to the same SQLite database
            var factory2 = new SqliteConnectionFactory(dbPath);
            var repo2 = new SqliteDownloadRepository(factory2);
            await repo2.InitializeAsync();

            var recovered = await repo2.GetByIdAsync(job.Id);

            Assert.NotNull(recovered);
            Assert.Equal(job.Id, recovered.Id);
            Assert.Equal("file.zip", recovered.FileName);
            Assert.Equal(524288, recovered.DownloadedBytes);
            Assert.Equal(1048576, recovered.TotalBytes);
            Assert.Equal(JobStatus.Paused, recovered.Status);
            Assert.Equal("\"abc123etag\"", recovered.ETag);
            Assert.Equal(2, recovered.Segments.Count);
            Assert.Equal(SegmentStatus.Completed, recovered.Segments[0].Status);
            Assert.Equal(SegmentStatus.Pending, recovered.Segments[1].Status);

            // Test delete cascading
            await repo2.DeleteJobAsync(job.Id);
            var deleted = await repo2.GetByIdAsync(job.Id);
            Assert.Null(deleted);

            var remainingSegments = await repo2.GetSegmentsAsync(job.Id);
            Assert.Empty(remainingSegments);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task SettingsRepository_SavesAndLoadsConfiguration()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"ydm_settings_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var repo = new SqliteSettingsRepository(factory);
            await repo.InitializeAsync();

            var settings = new ApplicationSettings
            {
                DefaultDownloadDirectory = @"D:\CustomDownloads",
                MaxConcurrentDownloads = 4,
                DefaultConnectionsPerDownload = 16,
                AutoStartDownloads = false,
                AllowLocalIpAddresses = true
            };

            await repo.SaveSettingsAsync(settings);

            var loaded = await repo.GetSettingsAsync();
            Assert.Equal(@"D:\CustomDownloads", loaded.DefaultDownloadDirectory);
            Assert.Equal(4, loaded.MaxConcurrentDownloads);
            Assert.Equal(16, loaded.DefaultConnectionsPerDownload);
            Assert.False(loaded.AutoStartDownloads);
            Assert.True(loaded.AllowLocalIpAddresses);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
