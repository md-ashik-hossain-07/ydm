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

    [Fact]
    public void DownloadSegment_UnknownLength_IsNotPrematurelyComplete()
    {
        var segment = new DownloadSegment
        {
            Index = 0,
            StartOffset = 0,
            CurrentOffset = 0,
            EndOffset = -1,
            Status = SegmentStatus.Pending
        };

        // When EndOffset is -1 (unknown length stream), it must NOT be complete at 0 bytes!
        Assert.False(segment.IsComplete);

        segment.CurrentOffset = 1024;
        Assert.False(segment.IsComplete);

        segment.Status = SegmentStatus.Completed;
        Assert.True(segment.IsComplete);
    }

    [Fact]
    public async Task DownloadEngine_ExecutesUnknownLengthChunkedStream_Successfully()
    {
        using var server = new MockHttpServer(dataSize: 64 * 1024);
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_chunked_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: $"{server.BaseUrl}unknown-length",
                destinationDirectory: testDir,
                customFileName: "chunked_test.bin");

            Assert.Single(job.Segments);
            Assert.False(job.Segments[0].IsComplete);

            await engine.StartAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.True(File.Exists(finishedJob.DestinationPath));

            byte[] downloaded = await File.ReadAllBytesAsync(finishedJob.DestinationPath);
            Assert.Equal(server.TestData.Length, downloaded.Length);
            Assert.Equal(SHA256.HashData(server.TestData), SHA256.HashData(downloaded));
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
    public async Task DownloadEngine_Http403Forbidden_ProducesMeaningfulErrorMessage()
    {
        using var server = new MockHttpServer();
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_403_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: $"{server.BaseUrl}forbidden",
                destinationDirectory: testDir,
                customFileName: "forbidden.bin");

            await engine.StartAsync(job.Id);

            var failedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(failedJob);
            Assert.Equal(JobStatus.Failed, failedJob.Status);
            Assert.False(string.IsNullOrWhiteSpace(failedJob.ErrorMessage));
            Assert.Contains("403", failedJob.ErrorMessage);
            Assert.DoesNotContain("Segment temp file not found", failedJob.ErrorMessage);
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
    public async Task DownloadEngine_LiveW3cDummyPdf_DownloadsAndVerifiesIntegrity()
    {
        string liveUrl = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_w3c_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        string dbPath = Path.Combine(testDir, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteDownloadRepository(connectionFactory);
        await repo.InitializeAsync();

        var engine = new DownloadEngine(repo);

        try
        {
            var job = await engine.AddDownloadAsync(
                url: liveUrl,
                destinationDirectory: testDir);

            Assert.Equal("dummy.pdf", job.FileName);
            Assert.Equal(13264, job.TotalBytes);
            Assert.True(job.IsResumable);

            await engine.StartAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.Equal(13264, finishedJob.DownloadedBytes);
            Assert.True(File.Exists(finishedJob.DestinationPath));
            Assert.Equal(13264, new FileInfo(finishedJob.DestinationPath).Length);
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
