using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using YDM.Core;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Storage.Migrations;

namespace YDM.Storage.Repositories;

public class SqliteDownloadRepository : IDownloadRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteDownloadRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync()
    {
        var migrationService = new MigrationService(_connectionFactory);
        await migrationService.ApplyMigrationsAsync();
    }

    public async Task<DownloadJob?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Downloads WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var job = MapJob(reader);
            job.Segments = (List<DownloadSegment>)await GetSegmentsInternalAsync(conn, job.Id);
            return job;
        }

        return null;
    }

    public async Task<IReadOnlyList<DownloadJob>> GetAllAsync()
    {
        var list = new List<DownloadJob>();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Downloads ORDER BY CreatedAt DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapJob(reader));
        }

        foreach (var job in list)
        {
            job.Segments = (List<DownloadSegment>)await GetSegmentsInternalAsync(conn, job.Id);
        }

        return list;
    }

    public async Task SaveJobAsync(DownloadJob job)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO Downloads (
                Id, Url, Referrer, Cookies, UserAgent, FileName, DestinationPath, TempDirectory,
                TotalBytes, DownloadedBytes, IsResumable, ETag, LastModified, Status, Priority,
                MaxConnections, CreatedAt, CompletedAt, ErrorMessage
            ) VALUES (
                @Id, @Url, @Referrer, @Cookies, @UserAgent, @FileName, @DestinationPath, @TempDirectory,
                @TotalBytes, @DownloadedBytes, @IsResumable, @ETag, @LastModified, @Status, @Priority,
                @MaxConnections, @CreatedAt, @CompletedAt, @ErrorMessage
            )
            ON CONFLICT(Id) DO UPDATE SET
                Url = excluded.Url,
                Referrer = excluded.Referrer,
                Cookies = excluded.Cookies,
                UserAgent = excluded.UserAgent,
                FileName = excluded.FileName,
                DestinationPath = excluded.DestinationPath,
                TempDirectory = excluded.TempDirectory,
                TotalBytes = excluded.TotalBytes,
                DownloadedBytes = excluded.DownloadedBytes,
                IsResumable = excluded.IsResumable,
                ETag = excluded.ETag,
                LastModified = excluded.LastModified,
                Status = excluded.Status,
                Priority = excluded.Priority,
                MaxConnections = excluded.MaxConnections,
                CompletedAt = excluded.CompletedAt,
                ErrorMessage = excluded.ErrorMessage;
        ";

        cmd.Parameters.AddWithValue("@Id", job.Id.ToString("D"));
        cmd.Parameters.AddWithValue("@Url", job.Url);
        cmd.Parameters.AddWithValue("@Referrer", (object?)job.Referrer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Cookies", (object?)job.Cookies ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserAgent", (object?)job.UserAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FileName", job.FileName);
        cmd.Parameters.AddWithValue("@DestinationPath", job.DestinationPath);
        cmd.Parameters.AddWithValue("@TempDirectory", job.TempDirectory);
        cmd.Parameters.AddWithValue("@TotalBytes", (object?)job.TotalBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DownloadedBytes", job.DownloadedBytes);
        cmd.Parameters.AddWithValue("@IsResumable", job.IsResumable ? 1 : 0);
        cmd.Parameters.AddWithValue("@ETag", (object?)job.ETag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastModified", job.LastModified.HasValue ? job.LastModified.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (int)job.Status);
        cmd.Parameters.AddWithValue("@Priority", (int)job.Priority);
        cmd.Parameters.AddWithValue("@MaxConnections", job.MaxConnections);
        cmd.Parameters.AddWithValue("@CreatedAt", job.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@CompletedAt", job.CompletedAt.HasValue ? job.CompletedAt.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)job.ErrorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();

        if (job.Segments.Count > 0)
        {
            await SaveSegmentsInternalAsync(conn, transaction, job.Id, job.Segments);
        }

        transaction.Commit();
    }

    public async Task UpdateJobStatusAsync(Guid id, JobStatus status, string? errorMessage = null)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (status == JobStatus.Completed)
        {
            cmd.CommandText = @"
                UPDATE Downloads 
                SET Status = @Status, CompletedAt = @CompletedAt, ErrorMessage = NULL 
                WHERE Id = @Id;
            ";
            cmd.Parameters.AddWithValue("@CompletedAt", DateTime.UtcNow.ToString("o"));
        }
        else
        {
            cmd.CommandText = @"
                UPDATE Downloads 
                SET Status = @Status, ErrorMessage = @ErrorMessage 
                WHERE Id = @Id;
            ";
            cmd.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        }

        cmd.Parameters.AddWithValue("@Status", (int)status);
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateProgressAsync(Guid id, long downloadedBytes, long speedBytesPerSecond = 0)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Downloads SET DownloadedBytes = @DownloadedBytes WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@DownloadedBytes", downloadedBytes);
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteJobAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Downloads WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveSegmentsAsync(Guid jobId, IEnumerable<DownloadSegment> segments)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        await SaveSegmentsInternalAsync(conn, transaction, jobId, segments);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<DownloadSegment>> GetSegmentsAsync(Guid jobId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await GetSegmentsInternalAsync(conn, jobId);
    }

    public async Task UpdateSegmentProgressAsync(Guid segmentId, long currentOffset, SegmentStatus status)
    {
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Segments 
            SET CurrentOffset = @CurrentOffset, Status = @Status 
            WHERE Id = @Id;
        ";
        cmd.Parameters.AddWithValue("@CurrentOffset", currentOffset);
        cmd.Parameters.AddWithValue("@Status", (int)status);
        cmd.Parameters.AddWithValue("@Id", segmentId.ToString("D"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<DownloadSegment>> GetSegmentsInternalAsync(SqliteConnection conn, Guid jobId)
    {
        var list = new List<DownloadSegment>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Segments WHERE JobId = @JobId ORDER BY SegmentIndex ASC;";
        cmd.Parameters.AddWithValue("@JobId", jobId.ToString("D"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DownloadSegment
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                JobId = jobId,
                Index = reader.GetInt32(reader.GetOrdinal("SegmentIndex")),
                StartOffset = reader.GetInt64(reader.GetOrdinal("StartOffset")),
                EndOffset = reader.GetInt64(reader.GetOrdinal("EndOffset")),
                CurrentOffset = reader.GetInt64(reader.GetOrdinal("CurrentOffset")),
                Status = (SegmentStatus)reader.GetInt32(reader.GetOrdinal("Status")),
                TempFilePath = reader.GetString(reader.GetOrdinal("TempFilePath"))
            });
        }

        return list;
    }

    private static async Task SaveSegmentsInternalAsync(SqliteConnection conn, SqliteTransaction transaction, Guid jobId, IEnumerable<DownloadSegment> segments)
    {
        using var delCmd = conn.CreateCommand();
        delCmd.Transaction = transaction;
        delCmd.CommandText = "DELETE FROM Segments WHERE JobId = @JobId;";
        delCmd.Parameters.AddWithValue("@JobId", jobId.ToString("D"));
        await delCmd.ExecuteNonQueryAsync();

        foreach (var seg in segments)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Segments (Id, JobId, SegmentIndex, StartOffset, EndOffset, CurrentOffset, Status, TempFilePath)
                VALUES (@Id, @JobId, @SegmentIndex, @StartOffset, @EndOffset, @CurrentOffset, @Status, @TempFilePath);
            ";
            cmd.Parameters.AddWithValue("@Id", seg.Id.ToString("D"));
            cmd.Parameters.AddWithValue("@JobId", jobId.ToString("D"));
            cmd.Parameters.AddWithValue("@SegmentIndex", seg.Index);
            cmd.Parameters.AddWithValue("@StartOffset", seg.StartOffset);
            cmd.Parameters.AddWithValue("@EndOffset", seg.EndOffset);
            cmd.Parameters.AddWithValue("@CurrentOffset", seg.CurrentOffset);
            cmd.Parameters.AddWithValue("@Status", (int)seg.Status);
            cmd.Parameters.AddWithValue("@TempFilePath", seg.TempFilePath);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static DownloadJob MapJob(DbDataReader reader)
    {
        return new DownloadJob
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Url = reader.GetString(reader.GetOrdinal("Url")),
            Referrer = reader.IsDBNull(reader.GetOrdinal("Referrer")) ? null : reader.GetString(reader.GetOrdinal("Referrer")),
            Cookies = reader.IsDBNull(reader.GetOrdinal("Cookies")) ? null : reader.GetString(reader.GetOrdinal("Cookies")),
            UserAgent = reader.IsDBNull(reader.GetOrdinal("UserAgent")) ? null : reader.GetString(reader.GetOrdinal("UserAgent")),
            FileName = reader.GetString(reader.GetOrdinal("FileName")),
            DestinationPath = reader.GetString(reader.GetOrdinal("DestinationPath")),
            TempDirectory = reader.GetString(reader.GetOrdinal("TempDirectory")),
            TotalBytes = reader.IsDBNull(reader.GetOrdinal("TotalBytes")) ? null : reader.GetInt64(reader.GetOrdinal("TotalBytes")),
            DownloadedBytes = reader.GetInt64(reader.GetOrdinal("DownloadedBytes")),
            IsResumable = reader.GetInt32(reader.GetOrdinal("IsResumable")) == 1,
            ETag = reader.IsDBNull(reader.GetOrdinal("ETag")) ? null : reader.GetString(reader.GetOrdinal("ETag")),
            LastModified = reader.IsDBNull(reader.GetOrdinal("LastModified")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastModified")), CultureInfo.InvariantCulture),
            Status = (JobStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            Priority = (DownloadPriority)reader.GetInt32(reader.GetOrdinal("Priority")),
            MaxConnections = reader.GetInt32(reader.GetOrdinal("MaxConnections")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")), CultureInfo.InvariantCulture),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("CompletedAt")), CultureInfo.InvariantCulture),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage"))
        };
    }
}
