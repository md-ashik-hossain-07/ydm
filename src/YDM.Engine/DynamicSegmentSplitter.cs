using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YDM.Core;
using YDM.Core.Models;

namespace YDM.Engine;

public class DynamicSegmentSplitter
{
    public const long DefaultMinSplitSize = 2 * 1024 * 1024; // 2 MB

    public static bool TrySplitSegment(
        DownloadJob job, 
        long minSplitSize, 
        out DownloadSegment? splitSource, 
        out DownloadSegment? newSegment)
    {
        splitSource = null;
        newSegment = null;

        if (!job.IsResumable || !job.TotalBytes.HasValue || job.TotalBytes.Value <= 0)
            return false;

        if (job.Segments.Count >= job.MaxConnections)
            return false;

        // Find active downloading or pending segment with the largest remaining bytes
        var candidate = job.Segments
            .Where(s => s.Status == SegmentStatus.Downloading || s.Status == SegmentStatus.Pending)
            .Select(s => new { Segment = s, Remaining = s.EndOffset - s.CurrentOffset })
            .Where(x => x.Remaining >= minSplitSize)
            .OrderByDescending(x => x.Remaining)
            .Select(x => x.Segment)
            .FirstOrDefault();

        if (candidate == null)
            return false;

        long currentRemaining = candidate.EndOffset - candidate.CurrentOffset;
        if (currentRemaining < minSplitSize)
            return false;

        long midpoint = candidate.CurrentOffset + (currentRemaining / 2);

        // Safety check
        if (midpoint <= candidate.CurrentOffset || midpoint > candidate.EndOffset)
            return false;

        long oldEndOffset = candidate.EndOffset;
        int nextIndex = job.Segments.Count;

        // Create new segment taking the second half
        var created = new DownloadSegment
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            Index = nextIndex,
            StartOffset = midpoint,
            CurrentOffset = midpoint,
            EndOffset = oldEndOffset,
            Status = SegmentStatus.Pending,
            TempFilePath = Path.Combine(job.TempDirectory, $"segment_{nextIndex}.tmp")
        };

        // Adjust candidate to end right before the midpoint
        candidate.EndOffset = midpoint - 1;

        splitSource = candidate;
        newSegment = created;
        job.Segments.Add(created);

        return true;
    }

    public static List<DownloadSegment> CreateInitialSegments(DownloadJob job, int connectionCount)
    {
        var segments = new List<DownloadSegment>();

        if (!job.TotalBytes.HasValue || job.TotalBytes.Value <= 0 || !job.IsResumable || connectionCount <= 1)
        {
            // Single stream segment
            segments.Add(new DownloadSegment
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Index = 0,
                StartOffset = 0,
                CurrentOffset = 0,
                EndOffset = job.TotalBytes.HasValue && job.TotalBytes.Value > 0 ? job.TotalBytes.Value - 1 : -1,
                Status = SegmentStatus.Pending,
                TempFilePath = Path.Combine(job.TempDirectory, "segment_0.tmp")
            });
            return segments;
        }

        long total = job.TotalBytes.Value;
        int actualConnections = Math.Clamp(connectionCount, 1, job.MaxConnections);
        long chunkSize = total / actualConnections;

        for (int i = 0; i < actualConnections; i++)
        {
            long start = i * chunkSize;
            long end = (i == actualConnections - 1) ? (total - 1) : ((i + 1) * chunkSize - 1);

            segments.Add(new DownloadSegment
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Index = i,
                StartOffset = start,
                CurrentOffset = start,
                EndOffset = end,
                Status = SegmentStatus.Pending,
                TempFilePath = Path.Combine(job.TempDirectory, $"segment_{i}.tmp")
            });
        }

        return segments;
    }
}
