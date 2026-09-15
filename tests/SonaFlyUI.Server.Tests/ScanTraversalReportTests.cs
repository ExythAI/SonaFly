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
}
