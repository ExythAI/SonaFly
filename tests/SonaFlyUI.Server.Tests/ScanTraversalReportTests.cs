using SonaFlyUI.Server.Application.DTOs;
using Xunit;

namespace SonaFlyUI.Server.Tests;

public sealed class ScanTraversalReportTests
{
    [Fact]
    public void AFileThatCouldNotBeInspectedIsNotTreatedAsAbsent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sonafly-report", "album");
        var inaccessibleFile = Path.Combine(directory, "song.mp3");
        var otherFile = Path.Combine(directory, "deleted.mp3");
        var report = new ScanTraversalReport();

        report.MarkRootAvailable(true);
        report.MarkDirectoryTraversed(directory);
        report.RecordFileFailure(inaccessibleFile, "Access denied");

        Assert.False(report.CanVouchForAbsenceOf(inaccessibleFile));
        Assert.True(report.CanVouchForAbsenceOf(otherFile));
    }

    [Fact]
    public void ATraversedParentConfirmsThatADeletedChildDirectoryIsAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "sonafly-report");
        var deletedTrack = Path.Combine(root, "deleted-album", "song.mp3");
        var report = new ScanTraversalReport();

        report.MarkRootAvailable(true);
        report.MarkDirectoryTraversed(root);

        Assert.True(report.CanVouchForAbsenceOf(deletedTrack));
    }

    [Fact]
    public void FailedOrSkippedSubtreesOverrideATraversedAncestor()
    {
        var root = Path.Combine(Path.GetTempPath(), "sonafly-report");
        var failed = Path.Combine(root, "offline-album");
        var skipped = Path.Combine(root, "linked-album");
        var report = new ScanTraversalReport();

        report.MarkRootAvailable(true);
        report.MarkDirectoryTraversed(root);
        report.RecordDirectoryFailure(failed, "Network error");
        report.MarkDirectoryUnverified(skipped);

        Assert.False(report.CanVouchForAbsenceOf(Path.Combine(failed, "song.mp3")));
        Assert.False(report.CanVouchForAbsenceOf(Path.Combine(skipped, "song.mp3")));
    }
}
