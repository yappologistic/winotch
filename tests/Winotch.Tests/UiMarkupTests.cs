namespace Winotch.Tests;

public sealed class UiMarkupTests
{
    [Fact]
    public void LiveStripUsesOnlyPrimaryTimerProgress()
    {
        var xaml = ReadRepoFile("src", "Winotch", "LiveStripPanel.xaml");

        Assert.DoesNotContain("TimerRing", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TimerActions", xaml, StringComparison.Ordinal);
        Assert.Contains("ActivityProgress", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedTimerShowsTransientTimerControls()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");

        Assert.Contains("LiveTimerRunningPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveTimerRemainingText", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelLiveTimer_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandBarResultsHaveReadableLayoutConstraints()
    {
        var xaml = ReadRepoFile("src", "Winotch", "CommandBar", "CommandBarPanel.xaml");

        Assert.Contains("MinHeight=\"50\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"112\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandBarCanBeDismissedFromMainWindowWithEscape()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var code = ReadRepoFile("src", "Winotch", "MainWindow.CommandBar.cs");

        Assert.Contains("KeyDown=\"Window_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HideCommandBar(restoreShell: true)", code, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshCommandBarResultsAsync();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollViewersAutoHideScrollBarsUntilScrolled()
    {
        var appXaml = ReadRepoFile("src", "Winotch", "App.xaml");
        var appCode = ReadRepoFile("src", "Winotch", "App.xaml.cs");

        Assert.Contains("AutoHideScrollViewer_Loaded", appXaml, StringComparison.Ordinal);
        Assert.Contains("AutoHideScrollViewer_ScrollChanged", appXaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ScrollBar}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SetScrollBarsOpacity(viewer, 0)", appCode, StringComparison.Ordinal);
        Assert.Contains("SetScrollBarsOpacity(viewer, 1)", appCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ShelfFlyout.xaml")]
    [InlineData("ColorPickerDroplet.xaml")]
    [InlineData("TextScrubberDroplet.xaml")]
    [InlineData("CameraMirrorWindow.xaml")]
    public void FlyoutHeadersUseRightAlignedCloseColumn(string fileName)
    {
        var xaml = ReadRepoFile("src", "Winotch", fileName);

        Assert.Contains("<ColumnDefinition Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Close", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DockPanel.Dock=\"Right\" Style=\"{StaticResource MediaIconButton}\"", xaml, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Winotch.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
