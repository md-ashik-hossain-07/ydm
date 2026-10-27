using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Core.Security;

namespace YDM.Engine;

public class FileAssembler : IFileAssembler
{
    private const int BufferSize = 128 * 1024; // 128 KB buffer

    public async Task<bool> AssembleFileAsync(
        DownloadJob job, 
        IProgress<double>? progress = null, 
        CancellationToken ct = default)
    {
        if (job.Segments.Count == 0)
            return false;

        var orderedSegments = job.Segments.OrderBy(s => s.Index).ToList();

        // Single stream optimization: if there's only 1 segment and its temp file is already complete
        if (orderedSegments.Count == 1)
        {
            var singleSeg = orderedSegments[0];
            if (!File.Exists(singleSeg.TempFilePath))
            {
                throw new FileNotFoundException($"Segment temp file not found: {singleSeg.TempFilePath}");
            }

            string? targetDir = Path.GetDirectoryName(job.DestinationPath);
            if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (File.Exists(job.DestinationPath))
            {
                File.Delete(job.DestinationPath);
            }

            File.Move(singleSeg.TempFilePath, job.DestinationPath, overwrite: true);
            SecurityZoneIdentifier.AttachInternetZone(job.DestinationPath, job.Url, job.Referrer);

            try
            {
                if (Directory.Exists(job.TempDirectory))
                {
                    Directory.Delete(job.TempDirectory, recursive: true);
                }
            }
            catch { }

            progress?.Report(100.0);
            return true;
        }

        // Multi-segment assembly validation
        for (int i = 0; i < orderedSegments.Count; i++)
        {
            var seg = orderedSegments[i];
            if (!File.Exists(seg.TempFilePath))
            {
                throw new FileNotFoundException($"Missing segment temporary file: {seg.TempFilePath}");
            }

            long fileLen = new FileInfo(seg.TempFilePath).Length;
            if (fileLen < seg.TotalBytes)
            {
                throw new InvalidOperationException($"Segment {seg.Index} is incomplete on disk. Expected {seg.TotalBytes} bytes, found {fileLen} bytes.");
            }
        }

        string tempAssembledPath = job.DestinationPath + ".ydm_part";
        string? destDir = Path.GetDirectoryName(job.DestinationPath);
        if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(tempAssembledPath))
        {
            File.Delete(tempAssembledPath);
        }

        long totalWritten = 0;
        long expectedTotal = job.TotalBytes ?? orderedSegments.Sum(s => s.TotalBytes);

        await using (var destStream = new FileStream(
            tempAssembledPath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            BufferSize, 
            useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];

            foreach (var seg in orderedSegments)
            {
                ct.ThrowIfCancellationRequested();

                await using var segStream = new FileStream(
                    seg.TempFilePath, 
                    FileMode.Open, 
                    FileAccess.Read, 
                    FileShare.Read, 
                    BufferSize, 
                    useAsync: true);

                int read;
                while ((read = await segStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await destStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalWritten += read;

                    if (expectedTotal > 0)
                    {
                        double p = Math.Clamp((double)totalWritten / expectedTotal * 100.0, 0.0, 100.0);
                        progress?.Report(p);
                    }
                }
            }

            await destStream.FlushAsync(ct);
        }

        // Atomic file move to final destination
        if (File.Exists(job.DestinationPath))
        {
            File.Delete(job.DestinationPath);
        }

        File.Move(tempAssembledPath, job.DestinationPath, overwrite: true);
        SecurityZoneIdentifier.AttachInternetZone(job.DestinationPath, job.Url, job.Referrer);

        // Clean up temporary segment files and directory
        foreach (var seg in orderedSegments)
        {
            try
            {
                if (File.Exists(seg.TempFilePath))
                {
                    File.Delete(seg.TempFilePath);
                }
            }
            catch { }
        }

        try
        {
            if (Directory.Exists(job.TempDirectory))
            {
                Directory.Delete(job.TempDirectory, recursive: true);
            }
        }
        catch { }

        progress?.Report(100.0);
        return true;
    }
}
