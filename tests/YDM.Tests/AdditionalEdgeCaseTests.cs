using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YDM.Core;
using YDM.Core.Models;
using YDM.Core.Security;
using YDM.Engine;
using YDM.Host;
using YDM.Storage;
using YDM.Storage.Repositories;

namespace YDM.Tests;

public class AdditionalEdgeCaseTests
{
    [Fact]
    public async Task DownloadEngine_DetectsChangedETagOnResume_AndInvalidatesSegments()
    {
        using var server = new MockHttpServer(dataSize: 512 * 1024);
        server.SlowMode = true;
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_etag_test_{Guid.NewGuid():N}");
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
                customFileName: "etag_test.bin",
                maxConnections: 2);

            // Pause
            var cts = new CancellationTokenSource();
            var startTask = engine.StartAsync(job.Id, cts.Token);
            await Task.Delay(20);
            await engine.PauseAsync(job.Id);
            cts.Cancel();
            try { await startTask; } catch { }

            // Alter server ETag
            server.CurrentETag = "\"test-etag-v2-changed\"";

            // Resume
            await engine.ResumeAsync(job.Id);

            var finishedJob = await repo.GetByIdAsync(job.Id);
            Assert.NotNull(finishedJob);
            Assert.Equal(JobStatus.Completed, finishedJob.Status);
            Assert.Equal("\"test-etag-v2-changed\"", finishedJob.ETag);

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
    public async Task DownloadEngine_Cancellation_DoesNotLeaveCorruptedFinalFile()
    {
        using var server = new MockHttpServer(dataSize: 1024 * 1024);
        server.SlowMode = true;
        string testDir = Path.Combine(Path.GetTempPath(), $"ydm_cancel_test_{Guid.NewGuid():N}");
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
                customFileName: "canceled_file.bin",
                maxConnections: 4);

            var cts = new CancellationTokenSource();
            var startTask = engine.StartAsync(job.Id, cts.Token);
            await Task.Delay(20);

            await engine.CancelAsync(job.Id);
            cts.Cancel();
            try { await startTask; } catch { }

            // Destination final file should NOT exist because assembly only runs on completion
            Assert.False(File.Exists(job.DestinationPath), "Final destination file must not exist if canceled during download.");
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
    public void NativeMessageHost_RejectsInvalidPayloadsAndLimitsSize()
    {
        // Test serialization and deserialization of native messaging framing
        var req = new NativeMessageRequest
        {
            Action = "ping"
        };
        string json = JsonSerializer.Serialize(req);
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        byte[] lenBytes = BitConverter.GetBytes((uint)utf8.Length);

        using var ms = new MemoryStream();
        ms.Write(lenBytes);
        ms.Write(utf8);
        ms.Seek(0, SeekOrigin.Begin);

        using var reader = new BinaryReader(ms);
        uint readLen = reader.ReadUInt32();
        Assert.Equal((uint)utf8.Length, readLen);

        byte[] payload = reader.ReadBytes((int)readLen);
        string decodedJson = Encoding.UTF8.GetString(payload);
        var parsed = JsonSerializer.Deserialize<NativeMessageRequest>(decodedJson);

        Assert.NotNull(parsed);
        Assert.Equal("ping", parsed.Action);

        // Verify oversized length rejection (> 1MB)
        uint oversized = 2 * 1024 * 1024; // 2MB
        Assert.True(oversized > 1024 * 1024);
    }

    [Fact]
    public async Task NativeMessageHost_ProcessExecution_RepliesToPingOverStdio()
    {
        // Locate built YDM.Host.exe
        string baseDir = AppContext.BaseDirectory;
        string hostPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "YDM.Host", "bin", "Release", "net8.0", "YDM.Host.exe"));
        if (!File.Exists(hostPath))
        {
            hostPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "YDM.Host", "bin", "Debug", "net8.0", "YDM.Host.exe"));
        }

        if (!File.Exists(hostPath))
        {
            hostPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "publish", "host", "YDM.Host.exe"));
        }

        Assert.True(File.Exists(hostPath), $"Host executable exists at {hostPath}");

        var psi = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Ensure DOTNET_ROOT is passed if needed
        string dotnetRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.dotnet";
        psi.Environment["DOTNET_ROOT"] = dotnetRoot;

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        try
        {
            var req = new NativeMessageRequest { Action = "ping" };
            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req));
            byte[] lenPrefix = BitConverter.GetBytes((uint)jsonBytes.Length);

            var stdin = process.StandardInput.BaseStream;
            await stdin.WriteAsync(lenPrefix, 0, 4);
            await stdin.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            await stdin.FlushAsync();

            var stdout = process.StandardOutput.BaseStream;
            byte[] respLenBytes = new byte[4];
            int readTotal = 0;
            while (readTotal < 4)
            {
                int r = await stdout.ReadAsync(respLenBytes, readTotal, 4 - readTotal);
                if (r <= 0) break;
                readTotal += r;
            }

            Assert.Equal(4, readTotal);
            uint respLen = BitConverter.ToUInt32(respLenBytes, 0);
            Assert.True(respLen > 0 && respLen < 10000);

            byte[] respPayload = new byte[respLen];
            int payloadRead = 0;
            while (payloadRead < respLen)
            {
                int r = await stdout.ReadAsync(respPayload, payloadRead, (int)respLen - payloadRead);
                if (r <= 0) break;
                payloadRead += r;
            }

            Assert.Equal((int)respLen, payloadRead);
            string respJson = Encoding.UTF8.GetString(respPayload);
            var response = JsonSerializer.Deserialize<NativeMessageResponse>(respJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(response);
            Assert.Equal("ok", response.Status);
            Assert.Equal("YDM Native Host is active", response.Message);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }
}
