using System;
using System.Linq;
using Xunit;
using YDM.Core;
using YDM.Core.Models;
using YDM.Engine;

namespace YDM.Tests;

public class DynamicSegmentSplitterTests
{
    [Fact]
    public void CreateInitialSegments_CreatesContiguousRangesWithoutGapsOrOverlaps()
    {
        var job = new DownloadJob
        {
            Id = Guid.NewGuid(),
            TotalBytes = 1000000,
            IsResumable = true,
            MaxConnections = 4,
            TempDirectory = @"C:\Temp"
        };

        var segments = DynamicSegmentSplitter.CreateInitialSegments(job, 4);

        Assert.Equal(4, segments.Count);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(999999, segments[3].EndOffset);

        for (int i = 0; i < segments.Count - 1; i++)
        {
            Assert.Equal(segments[i].EndOffset + 1, segments[i + 1].StartOffset);
        }

        long totalSum = segments.Sum(s => s.TotalBytes);
        Assert.Equal(1000000, totalSum);
    }

    [Fact]
    public void CreateInitialSegments_SingleStreamWhenNotResumable()
    {
        var job = new DownloadJob
        {
            Id = Guid.NewGuid(),
            TotalBytes = 50000,
            IsResumable = false,
            MaxConnections = 8,
            TempDirectory = @"C:\Temp"
        };

        var segments = DynamicSegmentSplitter.CreateInitialSegments(job, 8);
        Assert.Single(segments);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(49999, segments[0].EndOffset);
    }

    [Fact]
    public void TrySplitSegment_SplitsLargestRemainingChunkAtMidpoint()
    {
        var job = new DownloadJob
        {
            Id = Guid.NewGuid(),
            TotalBytes = 10 * 1024 * 1024, // 10 MB
            IsResumable = true,
            MaxConnections = 4,
            TempDirectory = @"C:\Temp"
        };

        // Initially 2 segments: 0 to 5MB-1, and 5MB to 10MB-1
        var seg1 = new DownloadSegment
        {
            Id = Guid.NewGuid(),
            Index = 0,
            StartOffset = 0,
            CurrentOffset = 4 * 1024 * 1024, // 4MB downloaded, 1MB remaining
            EndOffset = 5 * 1024 * 1024 - 1,
            Status = SegmentStatus.Downloading,
            TempFilePath = @"C:\Temp\s0.tmp"
        };

        var seg2 = new DownloadSegment
        {
            Id = Guid.NewGuid(),
            Index = 1,
            StartOffset = 5 * 1024 * 1024,
            CurrentOffset = 5 * 1024 * 1024, // 0 bytes downloaded, 5MB remaining
            EndOffset = 10 * 1024 * 1024 - 1,
            Status = SegmentStatus.Downloading,
            TempFilePath = @"C:\Temp\s1.tmp"
        };

        job.Segments.Add(seg1);
        job.Segments.Add(seg2);

        // Min split size = 2 MB
        bool split = DynamicSegmentSplitter.TrySplitSegment(job, 2 * 1024 * 1024, out var source, out var created);

        Assert.True(split);
        Assert.NotNull(source);
        Assert.NotNull(created);
        Assert.Equal(seg2.Id, source.Id); // seg2 had 5 MB remaining, larger than seg1's 1 MB

        // Verify boundary continuity
        Assert.Equal(source.EndOffset + 1, created.StartOffset);
        Assert.Equal(10 * 1024 * 1024 - 1, created.EndOffset);

        // Verify total byte preservation
        long expectedEnd = 10 * 1024 * 1024 - 1;
        Assert.Equal(expectedEnd, created.EndOffset);
        Assert.Equal(3, job.Segments.Count);
    }
}
