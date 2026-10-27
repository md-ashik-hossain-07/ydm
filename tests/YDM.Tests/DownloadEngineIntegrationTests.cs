using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YDM.Core;
using YDM.Core.Models;
using YDM.Engine;
using YDM.Storage;
using YDM.Storage.Repositories;

namespace YDM.Tests;

public class DownloadEngineIntegrationTests
{
    [Fact]
    public async Task HttpProbeService_DetectsRangeAndRedirect()
    {
        using var server = new MockHttpServer();
        var probe = new HttpProbeService();

        var standardProbe = await probe.ProbeAsync($"{server.BaseUrl}standard");
        Assert.True(standardProbe.IsSuccess);
        Assert.True(standardProbe.SupportsRange);
        Assert.Equal(server.TestData.Length, standardProbe.ContentLength);
        Assert.Equal("\"test-etag-v1\"", standardProbe.ETag);

        var redirectProbe = await probe.ProbeAsync($"{server.BaseUrl}redirect");
        Assert.True(redirectProbe.IsSuccess);
        Assert.Equal("redirected_archive.zip", redirectProbe.SuggestedFileName);
    }

    [Fact]
    public async Task DownloadEngine_ExecutesMultiSegmentDownload_AndVerifiesIntegrity()
    {
        using var server = new MockHttpServer(dataSize: 512 * 1024); // 512 KB
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_int_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: $"{server.BaseUrl}standard",
                destinationDirectory: testDir,
                customFileName: "multi_test.bin",
                maxConnections: 4);

            Assert.Equal(4, job.Segments.Count);

            // Execute download
            await engine.StartAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.True(File.Exists(finishedJob.DestinationPath));

            byte[] downloadedBytes = await File.ReadAllBytesAsync(finishedJob.DestinationPath);
            Assert.Equal(server.TestData.Length, downloadedBytes.Length);

            // Verify bit-exact cryptographic SHA-256 match
            byte[] expectedHash = SHA256.HashData(server.TestData);
            byte[] actualHash = SHA256.HashData(downloadedBytes);
            Assert.Equal(expectedHash, actualHash);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task DownloadEngine_ExecutesSingleStreamDownload_WhenNoRangeSupport()
    {
        using var server = new MockHttpServer(dataSize: 256 * 1024); // 256 KB
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_norange_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: $"{server.BaseUrl}no-range",
                destinationDirectory: testDir,
                customFileName: "single_test.bin",
                maxConnections: 8);

            Assert.Single(job.Segments); // Must gracefully degrade to 1 segment

            await engine.StartAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.True(File.Exists(finishedJob.DestinationPath));

            byte[] downloadedBytes = await File.ReadAllBytesAsync(finishedJob.DestinationPath);
            Assert.Equal(server.TestData.Length, downloadedBytes.Length);
            Assert.Equal(SHA256.HashData(server.TestData), SHA256.HashData(downloadedBytes));
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task DownloadEngine_PausesAndResumes_WithoutCorruption()
    {
        using var server = new MockHttpServer(dataSize: 1024 * 1024); // 1 MB
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_pause_resume_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: $"{server.BaseUrl}standard",
                destinationDirectory: testDir,
                customFileName: "resume_test.bin",
                maxConnections: 2);

            // Pause after start using CancellationToken
            var cts = new CancellationTokenSource();
            var startTask = engine.StartAsync(job.Id, cts.Token);
            
            // Allow bytes to flow
            await Task.Delay(30);
            await engine.PauseAsync(job.Id);
            cts.Cancel();

            try { await startTask; } catch { }

            var pausedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(pausedJob);

            // Resume the download to completion
            await engine.ResumeAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.True(File.Exists(finishedJob.DestinationPath));

            byte[] downloadedBytes = await File.ReadAllBytesAsync(finishedJob.DestinationPath);
            Assert.Equal(server.TestData.Length, downloadedBytes.Length);
            Assert.Equal(SHA256.HashData(server.TestData), SHA256.HashData(downloadedBytes));
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }
    }
}
