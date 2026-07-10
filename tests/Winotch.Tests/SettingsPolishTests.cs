using System.Xml.Linq;

namespace Winotch.Tests;

public class SettingsPolishTests
{
    [Fact]
    public void SettingsWindowSourceKeepsRoomyToggleRowsAndAccessibleInputs()
    {
        var xaml = ReadRepoFile("src", "Winotch", "SettingsWindow.xaml");
        var doc = XDocument.Parse(xaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var toggleStyle = doc.Descendants(ui + "Style")
            .Single(style => (string?)style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "SettingsToggle");

        Assert.Contains(toggleStyle.Descendants(ui + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "MinWidth" &&
            (string?)setter.Attribute("Value") == "44");
        Assert.Equal("ToggleSwitch", (string?)toggleStyle.Attribute("TargetType"));
        Assert.Single(doc.Descendants(ui + "DesktopAcrylicBackdrop"));
        Assert.True(doc.Descendants(ui + "ToggleSwitch").Count() >= 10);
        Assert.Contains("AutomationProperties.Name=\"Toast duration scale\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"ICS subscription URLs\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Request notification access\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Copy diagnostics\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("Click=\"RequestNotificationAccessClick\"", xaml);
        Assert.Contains("Click=\"CopyDiagnosticsClick\"", xaml);
        Assert.Contains("Toggled=\"", xaml);
        Assert.DoesNotContain("DropShadowEffect", xaml);
        Assert.DoesNotContain("ControlTemplate", xaml);
        Assert.DoesNotContain("Trigger", xaml);
    }

    [Fact]
    public void SettingsWindowIconUsesIncludedTrayAsset()
    {
        var project = XDocument.Parse(ReadRepoFile("src", "Winotch", "Winotch.csproj"));
        var tray = ReadRepoFile("src", "Winotch", "NativeTrayWindow.cs");

        Assert.Contains(project.Descendants("Resource"), resource =>
            (string?)resource.Attribute("Include") == "Resources\\WinotchTray.ico");
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"Resources\", \"WinotchTray.ico\")", tray);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src", "Winotch", "Resources", "WinotchTray.ico")));
    }

    [Fact]
    public void ProductDocsDescribeMiniOnlyForegroundAndPrivacySurfaces()
    {
        var readme = ReadRepoFile("README.md");
        var architecture = ReadRepoFile("docs", "architecture.md");

        Assert.Contains("Foreground detection currently keeps the shell in Mini for every foreground app state", readme);
        Assert.Contains("Winotch requests notification history access only when the user clicks Request access in Settings", readme);
        Assert.Contains("Diagnostics export copies a local device and settings summary", readme);
        Assert.Contains("Clipboard history stays in memory", readme);
        Assert.Contains("`ForegroundWindowService.DecideMode` currently returns `Mini` for every foreground app state", architecture);
        Assert.Contains("Settings owns the explicit Request access button", architecture);
        Assert.Contains("NotificationService.RequestHistoryAccessAsync", architecture);
    }

    [Fact]
    public void ExpandedPanelKeepsScrollingLocalAndNotificationBodiesClamped()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var doc = XDocument.Parse(xaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.DoesNotContain(doc.Descendants(ui + "ScrollViewer"),
            element => (string?)element.Attribute(xamlName) == "DetailScrollViewer");

        var notificationList = doc.Descendants(ui + "ListView")
            .Single(element => (string?)element.Attribute(xamlName) == "NotificationList");
        Assert.Equal("Hidden", (string?)notificationList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Contains(doc.Descendants(ui + "Border"), element =>
            (string?)element.Attribute(xamlName) == "ActivityScrollIndicator" &&
            (string?)element.Attribute("Opacity") == "0");
        Assert.Contains(doc.Descendants(ui + "TranslateTransform"), element =>
            (string?)element.Attribute(xamlName) == "ActivityScrollIndicatorTransform");

        var notificationItem = notificationList.Descendants(ui + "StackPanel")
            .Single(element => (string?)element.Attribute("MaxHeight") == "68");
        var notificationBody = notificationList.Descendants(ui + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding Body}");
        Assert.Equal("32", (string?)notificationBody.Attribute("MaxHeight"));
        Assert.Equal("Wrap", (string?)notificationBody.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)notificationBody.Attribute("TextTrimming"));
    }

    [Fact]
    public void ExpandedPanelUsesAlignedStatsRows()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var doc = XDocument.Parse(xaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        foreach (var rowName in new[] { "StatsCpuRow", "StatsRamRow", "StatsNetRow" })
        {
            var row = doc.Descendants(ui + "Grid")
                .Single(element => (string?)element.Attribute(xamlName) == rowName);
            var columns = row.Descendants(ui + "ColumnDefinition").ToArray();

            Assert.Equal("48", (string?)columns[0].Attribute("Width"));
            Assert.Equal("*", (string?)columns[1].Attribute("Width"));
            Assert.Contains(row.Descendants(ui + "TextBlock"), text =>
                (string?)text.Attribute("Grid.Column") == "1" &&
                (string?)text.Attribute("TextAlignment") == "Right");
        }
    }

    [Fact]
    public void ExpandedControlsUseCompactNativeSections()
    {
        var xaml = ReadRepoFile("src", "Winotch", "MainWindow.xaml");
        var appXaml = ReadRepoFile("src", "Winotch", "App.xaml");
        var doc = XDocument.Parse(xaml);
        var appDoc = XDocument.Parse(appXaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var xamlKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

        var audioSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "AudioControlsSection");
        var brightnessSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "BrightnessBlock");
        var wifiSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "WifiControlsSection");

        Assert.Equal("{StaticResource FluentSectionCard}", (string?)audioSection.Attribute("Style"));
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "OutputDeviceList");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "SelectedOutputDeviceText");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "VolumeSlider");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "AudioSessionMixerSection");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "MicMuteButton");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "SystemRail");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "ModeTabs");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "ControlsModeTab");
        Assert.Contains(doc.Descendants(ui + "TextBlock"), element =>
            (string?)element.Attribute(xamlName) == "NowModeText" &&
            (string?)element.Attribute("Text") == "Timer");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "TimerColumn");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "ActivityModeTab");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "ControlsTabContent");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "OpenSettingsButton");
        Assert.DoesNotContain(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "ActivityStrip");
        Assert.DoesNotContain(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "CopyDiagnosticsButton");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "NowPlayingSection");
        Assert.Contains(doc.Descendants(), element => (string?)element.Attribute(xamlName) == "NoMediaText");
        Assert.Contains(doc.Descendants(ui + "ScrollViewer"), element => (string?)element.Attribute(xamlName) == "AudioControlsScroll");
        Assert.DoesNotContain(doc.Descendants(ui + "ScrollViewer"), element => (string?)element.Attribute(xamlName) == "ActivityContentScroll");
        var nowSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "NowSection");
        var activitySection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "ActivitySection");
        var controlsContent = doc.Descendants(ui + "Grid")
            .Single(element => (string?)element.Attribute(xamlName) == "ControlsTabContent");
        Assert.Equal("{StaticResource FluentSectionCard}", (string?)nowSection.Attribute("Style"));
        Assert.Contains(nowSection.Descendants(ui + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Timer");
        Assert.Contains(nowSection.Descendants(ui + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "Pomodoro");
        Assert.Contains(nowSection.Descendants(), element => (string?)element.Attribute(xamlName) == "FocusTimerSection");
        Assert.Contains(nowSection.Descendants(), element => (string?)element.Attribute(xamlName) == "FocusSetupPanel");
        Assert.Contains(nowSection.Descendants(), element => (string?)element.Attribute(xamlName) == "FocusRunningPanel");
        Assert.DoesNotContain(nowSection.Descendants(), element => (string?)element.Attribute(xamlName) == "MediaPanel");
        Assert.DoesNotContain(nowSection.Descendants(), element => (string?)element.Attribute(xamlName) == "NoMediaText");
        Assert.Equal("{StaticResource FluentSectionCard}", (string?)activitySection.Attribute("Style"));
        Assert.Equal("Collapsed", (string?)activitySection.Attribute("Visibility"));
        Assert.DoesNotContain(controlsContent.Descendants(), element => (string?)element.Attribute(xamlName) == "ActivitySection");
        Assert.DoesNotContain(activitySection.Descendants(), element => (string?)element.Attribute(xamlName) == "FocusSetupPanel");
        Assert.DoesNotContain(activitySection.Descendants(), element => (string?)element.Attribute(xamlName) == "FocusRunningPanel");
        Assert.Equal("TimerModeTab_Click", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "NowModeTab")
            .Attribute("Click"));
        Assert.Equal("TimerModeTab_PreviewMouseLeftButtonDown", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "NowModeTab")
            .Attribute("PointerPressed"));
        Assert.Equal("ControlsModeTab_Click", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "ControlsModeTab")
            .Attribute("Click"));
        Assert.Equal("ControlsModeTab_PreviewMouseLeftButtonDown", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "ControlsModeTab")
            .Attribute("PointerPressed"));
        Assert.Equal("ActivityModeTab_Click", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "ActivityModeTab")
            .Attribute("Click"));
        Assert.Equal("ActivityModeTab_PreviewMouseLeftButtonDown", (string?)doc.Descendants(ui + "Button")
            .Single(element => (string?)element.Attribute(xamlName) == "ActivityModeTab")
            .Attribute("PointerPressed"));
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "AudioMoreToggleButton");
        var audioMorePanel = audioSection.Descendants()
            .Single(element => (string?)element.Attribute(xamlName) == "AudioMorePanel");
        Assert.Equal("Collapsed", (string?)audioMorePanel.Attribute("Visibility"));
        Assert.Contains(audioSection.Descendants(ui + "Button"), button =>
            (string?)button.Attribute("Click") == "OutputDevice_Click" &&
            (string?)button.Attribute("HorizontalContentAlignment") == "Stretch");
        var defaultButtonStyle = appDoc.Descendants(ui + "Style")
            .Single(style => (string?)style.Attribute("TargetType") == "Button" && style.Attribute(xamlKey) is null);
        Assert.Contains(defaultButtonStyle.Descendants(ui + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "CornerRadius" &&
            (string?)setter.Attribute("Value") == "10");
        Assert.DoesNotContain(appDoc.Descendants(ui + "ControlTemplate"), _ => true);
        Assert.Contains(brightnessSection.Descendants(), element => (string?)element.Attribute(xamlName) == "BrightnessList");
        Assert.DoesNotContain(brightnessSection.Descendants(ui + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding Name}");
        Assert.Contains(brightnessSection.Descendants(ui + "Slider"), slider =>
            (string?)slider.Attribute("AutomationProperties.Name") == "{Binding Name}" &&
            (string?)slider.Attribute("ValueChanged") == "BrightnessSlider_ValueChanged");
        Assert.Contains(wifiSection.Descendants(), element => (string?)element.Attribute(xamlName) == "WifiList");
        Assert.Contains(wifiSection.Descendants(), element => (string?)element.Attribute(xamlName) == "ConnectWifiButton");
    }

    [Fact]
    public void ExpandedPanelStatusCopyStaysShort()
    {
        var mainWindow = ReadRepoFile("src", "Winotch", "MainWindow.xaml.cs");
        var notifications = ReadRepoFile("src", "Winotch", "NotificationService.cs");

        Assert.Contains("\"Connected. Location needed to scan Wi-Fi.\"", mainWindow);
        Assert.DoesNotContain("Scan needs Windows Location permission", mainWindow);
        Assert.Equal(2, mainWindow.Split("SetMouseTransparent(isFullBar && !_expanded)").Length - 1);
        Assert.Contains("WindowChromeInterop.SetMouseTransparent(this, enabled && !_expanded && DetailPanel.Opacity <= 0);", mainWindow);
        Assert.DoesNotContain("Notification listener is not available", notifications);
        Assert.DoesNotContain("Notification access unavailable:", notifications);
        Assert.Contains("\"Notification access unavailable.\"", notifications);
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), Path.Combine(parts)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Winotch.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
