using System.Xml.Linq;

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
        Assert.Contains("MaxHeight=\"224\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CommandResultItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"56\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource CommandResultItem}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"112\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", xaml, StringComparison.Ordinal);

        var code = ReadRepoFile("src", "Winotch", "MainWindow.CommandBar.cs");
        Assert.Contains("ResizeCommandBarForResults(results.Count)", code, StringComparison.Ordinal);
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
    public void AppResourcesDefineReferenceMatchedFluentTokens()
    {
        var appXaml = ReadRepoFile("src", "Winotch", "App.xaml");
        var doc = XDocument.Parse(appXaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var keys = doc.Descendants()
            .Select(element => (string?)element.Attribute(xamlKey))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal("Dark", (string?)doc.Root?.Attribute("RequestedTheme"));
        Assert.Contains("WinotchTextFont", keys);
        Assert.Contains("WinotchDisplayFont", keys);
        Assert.Contains("WinotchSurfaceColor", keys);
        Assert.Contains("WinotchSurfaceRaisedColor", keys);
        Assert.Contains("WinotchStrokeColor", keys);
        Assert.Contains("WinotchAccentColor", keys);
        Assert.Contains("WinotchPillCornerRadius", keys);
        Assert.Contains("WinotchCardCornerRadius", keys);
        Assert.Contains(doc.Descendants(ui + "Style"), style =>
            (string?)style.Attribute("TargetType") == "Button");
    }

    [Fact]
    public void ShellUsesNativeDesktopAcrylicAndReferenceGeometry()
    {
        var doc = XDocument.Parse(ReadRepoFile("src", "Winotch", "MainWindow.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var shell = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "NotchShell");

        Assert.Equal("FluentWindow", doc.Root?.Name.LocalName);
        Assert.Equal("using:Winotch", doc.Root?.GetNamespaceOfPrefix("local")?.NamespaceName);
        Assert.Equal("260", (string?)doc.Root?.Attribute("Width"));
        Assert.Equal("960", (string?)doc.Root?.Attribute("MinimumShellHostWidth"));
        Assert.Equal("520", (string?)doc.Root?.Attribute("MinimumShellHostHeight"));
        Assert.Equal("260", (string?)shell.Attribute("Width"));
        Assert.Equal("68", (string?)shell.Attribute("Height"));
        Assert.Equal("0,0,34,34", (string?)shell.Attribute("CornerRadius"));
        Assert.Equal("Center", (string?)shell.Attribute("HorizontalAlignment"));
        Assert.Equal("Transparent", (string?)shell.Attribute("BorderBrush"));
        Assert.Equal("0", (string?)shell.Attribute("BorderThickness"));
        Assert.Equal("{StaticResource NotchHitTestFill}", (string?)shell.Attribute("Background"));
        var backdropHost = Assert.Single(shell.Descendants(ui + "SystemBackdropElement"));
        Assert.Single(backdropHost.Descendants().Where(element =>
            element.Name.LocalName == "PersistentDesktopAcrylicBackdrop"));
    }

    [Fact]
    public void ShellModesSynchronizeVisibleBackdropAndNativeCornerRadii()
    {
        var code = ReadRepoFile("src", "Winotch", "MainWindow.xaml.cs");

        Assert.Contains("NotchShell.CornerRadius = corners", code, StringComparison.Ordinal);
        Assert.Contains("TabChrome.CornerRadius = corners", code, StringComparison.Ordinal);
        Assert.Contains("BottomCornerRadius = radius", code, StringComparison.Ordinal);
        Assert.Contains("SetShellCornerRadius(isFullBar ? 0 : isLive ? 38 : 34)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderKeepsClockOnOneCenteredLine()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var code = ReadRepoFile("src", "Winotch", "MainWindow.xaml.cs");

        Assert.DoesNotContain("x:Name=\"DateText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellAnimator.Hide(DateText", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellAnimator.Show(DateText", code, StringComparison.Ordinal);
        Assert.Contains("ClockGroup.HorizontalAlignment = isFullBar ? HorizontalAlignment.Left : HorizontalAlignment.Center", code, StringComparison.Ordinal);
        Assert.Contains("LargeDateText.Visibility = general.ShowDate", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FluentWindowSuppressesNativeOverlayBorderAndPreservesNativeMaximize()
    {
        var code = ReadRepoFile("src", "Winotch", "FluentWindow.cs");

        Assert.Contains("DwmwaBorderColor", code, StringComparison.Ordinal);
        Assert.Contains("DwmColorNone", code, StringComparison.Ordinal);
        Assert.Contains("NormalizeOverlayWindowStyle", code, StringComparison.Ordinal);
        Assert.Contains("WsPopup", code, StringComparison.Ordinal);
        Assert.Contains("SwpFrameChanged", code, StringComparison.Ordinal);
        Assert.Contains("HwndTopmost", code, StringComparison.Ordinal);
        Assert.Contains("OverlappedPresenterState.Maximized", code, StringComparison.Ordinal);
        Assert.Contains("_visibleShellRegion", code, StringComparison.Ordinal);
        Assert.Contains("PrepareVisibleShellRegion", code, StringComparison.Ordinal);
        Assert.Contains("DwmwaNcRenderingPolicy", code, StringComparison.Ordinal);
        Assert.Contains("DwmwaTransitionsForcedDisabled", code, StringComparison.Ordinal);
        Assert.Contains("DwmwaWindowCornerPreference", code, StringComparison.Ordinal);

        var animator = ReadRepoFile("src", "Winotch", "ShellAnimator.cs");
        Assert.Contains(
            "ApplyShellGeometry(window, shell, displayedGeometry, host, current)",
            animator,
            StringComparison.Ordinal);
        Assert.Contains("ShellAnimationTiming.BackdropWarmupDuration", animator, StringComparison.Ordinal);
        Assert.Contains("new GeometryTransition(current, current", animator, StringComparison.Ordinal);
        Assert.Contains("window.FlushDwmComposition()", animator, StringComparison.Ordinal);
        Assert.Contains("warmupTimer.Start()", animator, StringComparison.Ordinal);
        Assert.Contains("window.ResolveShellHostGeometry(host)", animator, StringComparison.Ordinal);
        Assert.Contains("ApplyShellGeometry(window, shell, geometry, host)", animator, StringComparison.Ordinal);
        Assert.Contains("var shellHeight = Current(shell.Height, shell.ActualHeight)", animator, StringComparison.Ordinal);
        Assert.Contains("shellHeight,\n            shellHeight,", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("window.Height,\n            left,", animator, StringComparison.Ordinal);
        Assert.True(
            animator.IndexOf("window.PrepareVisibleShellRegion", StringComparison.Ordinal) <
            animator.IndexOf("window.MoveAndResizeAtScale", StringComparison.Ordinal));
    }

    [Fact]
    public void FlyoutsDismissOnRealOwnerClicksButIgnoreOwnedPopups()
    {
        var code = ReadRepoFile("src", "Winotch", "FlyoutClosePolicy.cs");

        Assert.Contains("!IsAnyMouseButtonPressed()", code, StringComparison.Ordinal);
        Assert.Contains("!IsOwnedBy(foreground, flyoutHandle)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("flyout.Owner?.IsActive", code, StringComparison.Ordinal);
        Assert.DoesNotContain("processId != Environment.ProcessId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ShelfIsAPersistentDropTargetOnBothSurfaces()
    {
        var shelfXaml = ReadRepoFile("src", "Winotch", "ShelfFlyout.xaml");
        var notchXaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var shelfCode = ReadRepoFile("src", "Winotch", "ShelfFlyout.xaml.cs");
        var notchCode = ReadRepoFile("src", "Winotch", "MainWindow.Shelf.cs");
        var mainCode = ReadRepoFile("src", "Winotch", "MainWindow.xaml.cs");

        Assert.Contains("AllowDrop=\"True\"", shelfXaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"Shelf_DragOver\"", shelfXaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"Shelf_Drop\"", shelfXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellHost\"", notchXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", notchXaml, StringComparison.Ordinal);
        Assert.Contains("DragEnter=\"Notch_DragEnter\"", notchXaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"Notch_Drop\"", notchXaml, StringComparison.Ordinal);
        Assert.Contains("StageDropAsync(e.DataView", shelfCode, StringComparison.Ordinal);
        Assert.Contains("StageDropAsync(e.DataView", notchCode, StringComparison.Ordinal);
        Assert.Contains("e.GetDeferral()", shelfCode, StringComparison.Ordinal);
        Assert.Contains("e.GetDeferral()", notchCode, StringComparison.Ordinal);
        Assert.Contains("deferral.Complete()", shelfCode, StringComparison.Ordinal);
        Assert.Contains("deferral.Complete()", notchCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Window_Activated", shelfCode, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseShelfAndDropletsAsync", mainCode, StringComparison.Ordinal);
        Assert.DoesNotContain("new ShelfFlyout(_shelf) { Owner = this }", notchCode, StringComparison.Ordinal);
        Assert.Contains("!_settings.Current.Shelf.Enabled", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlUsesWinUiControlsWithoutWpfOnlyTemplatesOrTriggers()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(Path.Combine(root, "src", "Winotch"), "*.xaml", SearchOption.AllDirectories);

        Assert.NotEmpty(xamlFiles);
        foreach (var file in xamlFiles)
        {
            var xaml = File.ReadAllText(file);
            var doc = XDocument.Parse(xaml);
            Assert.Equal(
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation",
                doc.Root?.GetDefaultNamespace().NamespaceName);
            Assert.DoesNotContain("{x:Type", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<ControlTemplate", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<Trigger", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<EventTrigger", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("DockPanel", xaml, StringComparison.Ordinal);
        }

        var main = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        Assert.Contains("<ListView", main, StringComparison.Ordinal);
        Assert.Contains("<Slider", main, StringComparison.Ordinal);
        Assert.Contains("<ProgressBar", main, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=", main, StringComparison.Ordinal);

        var settings = ReadRepoFile("src", "Winotch", "SettingsWindow.xaml");
        Assert.Contains("<ToggleSwitch", settings, StringComparison.Ordinal);
        Assert.Contains("<ComboBox", settings, StringComparison.Ordinal);
        Assert.Contains("<local:PersistentDesktopAcrylicBackdrop", settings, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ShelfFlyout.xaml")]
    [InlineData("ColorPickerDroplet.xaml")]
    [InlineData("TextScrubberDroplet.xaml")]
    [InlineData("CameraMirrorWindow.xaml")]
    public void AuxiliaryFlyoutsUseNativeDesktopAcrylic(string fileName)
    {
        var xaml = ReadRepoFile("src", "Winotch", fileName);

        Assert.Contains("<local:PersistentDesktopAcrylicBackdrop", xaml, StringComparison.Ordinal);
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
