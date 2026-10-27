using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core;
using YDM.Core.Interfaces;
using YDM.Core.Models;

namespace YDM.Engine;

public class SegmentDownloader : ISegmentDownloader
{
    private const int BufferSize = 64 * 1024; // 64 KB buffer

    public async Task DownloadSegmentAsync(
        DownloadJob job, 
        DownloadSegment segment, 
        HttpClient httpClient, 
        IProgress<long> progress, 
        CancellationToken ct)
    {
        if (segment.IsComplete)
        {
            segment.Status = SegmentStatus.Completed;
            return;
        }

        segment.Status = SegmentStatus.Downloading;

        string? dir = Path.GetDirectoryName(segment.TempFilePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Verify existing file length matches (CurrentOffset - StartOffset)
        long existingLength = 0;
        if (File.Exists(segment.TempFilePath))
        {
            existingLength = new FileInfo(segment.TempFilePath).Length;
            long expectedLength = segment.CurrentOffset - segment.StartOffset;
            if (existingLength != expectedLength)
            {
                // Resynchronize CurrentOffset to match what is safely on disk
                segment.CurrentOffset = segment.StartOffset + existingLength;
            }
        }

        if (segment.IsComplete)
        {
            segment.Status = SegmentStatus.Completed;
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);

        if (job.IsResumable)
        {
            if (segment.EndOffset >= segment.CurrentOffset)
            {
                request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, segment.EndOffset);
            }
            else if (segment.EndOffset == -1)
            {
                request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, null);
            }

            if (!string.IsNullOrWhiteSpace(job.ETag))
            {
                request.Headers.TryAddWithoutValidation("If-Range", job.ETag);
            }
        }

        if (!string.IsNullOrWhiteSpace(job.Referrer))
        {
            request.Headers.Referrer = new Uri(job.Referrer);
        }

        if (!string.IsNullOrWhiteSpace(job.Cookies))
        {
            request.Headers.TryAddWithoutValidation("Cookie", job.Cookies);
        }

        if (!string.IsNullOrWhiteSpace(job.UserAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(job.UserAgent);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Range already satisfied
            segment.Status = SegmentStatus.Completed;
            return;
        }

        if (job.IsResumable && segment.CurrentOffset > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            // Server returned 200 OK instead of 206 Partial Content: ETag changed or server rejected range
            throw new InvalidOperationException("Server ignored byte range request or remote resource has changed.");
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"Server responded with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            segment.TempFilePath, 
            FileMode.OpenOrCreate, 
            FileAccess.Write, 
            FileShare.Read, 
            BufferSize, 
            useAsync: true);

        if (fileStream.Length > (segment.CurrentOffset - segment.StartOffset))
        {
            fileStream.SetLength(segment.CurrentOffset - segment.StartOffset);
        }
        fileStream.Seek(0, SeekOrigin.End);

        byte[] buffer = new byte[BufferSize];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            segment.CurrentOffset += bytesRead;
            progress.Report(bytesRead);

            if (segment.EndOffset >= 0 && segment.CurrentOffset > segment.EndOffset)
            {
                break;
            }
        }

        await fileStream.FlushAsync(ct);
        segment.Status = SegmentStatus.Completed;
    }
}
