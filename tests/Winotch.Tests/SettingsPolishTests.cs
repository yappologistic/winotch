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
            .Single(style => (string?)style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ToggleSwitch");

        Assert.Contains(toggleStyle.Descendants(ui + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "MinHeight" &&
            (string?)setter.Attribute("Value") == "38");
        var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var track = toggleStyle.Descendants(ui + "Border").Single(border => (string?)border.Attribute(xamlName) == "SwitchTrack");
        var thumb = toggleStyle.Descendants(ui + "Ellipse").Single(ellipse => (string?)ellipse.Attribute(xamlName) == "SwitchThumb");
        var trackHeight = double.Parse((string?)track.Attribute("Height") ?? "0");
        var borderThickness = double.Parse((string?)track.Attribute("BorderThickness") ?? "0");
        var thumbHeight = double.Parse((string?)thumb.Attribute("Height") ?? "0");
        var thumbMargin = double.Parse((string?)thumb.Attribute("Margin") ?? "0");

        Assert.True(thumbHeight + (thumbMargin * 2) <= trackHeight - (borderThickness * 2));
        Assert.Contains("AutomationProperties.Name=\"Toast duration scale\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"ICS subscription URLs\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Request notification access\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Copy diagnostics\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("Click=\"RequestNotificationAccessClick\"", xaml);
        Assert.Contains("Click=\"CopyDiagnosticsClick\"", xaml);
        Assert.Contains("Icon=\"Resources/WinotchTray.ico\"", xaml);
        Assert.DoesNotContain("DropShadowEffect", xaml);
    }

    [Fact]
    public void SettingsWindowIconUsesIncludedTrayAsset()
    {
        var xaml = XDocument.Parse(ReadRepoFile("src", "Winotch", "SettingsWindow.xaml"));
        var project = XDocument.Parse(ReadRepoFile("src", "Winotch", "Winotch.csproj"));

        Assert.Equal("Resources/WinotchTray.ico", (string?)xaml.Root?.Attribute("Icon"));
        Assert.Contains(project.Descendants("Resource"), resource =>
            (string?)resource.Attribute("Include") == "Resources\\WinotchTray.ico");
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

        var notificationList = doc.Descendants(ui + "ListBox")
            .Single(element => (string?)element.Attribute(xamlName) == "NotificationList");
        Assert.Equal("Auto", (string?)notificationList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));

        var notificationItem = notificationList.Descendants(ui + "StackPanel")
            .Single(element => (string?)element.Attribute("MaxHeight") == "85");
        var notificationBody = notificationList.Descendants(ui + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding Body}");
        Assert.Equal("True", (string?)notificationItem.Attribute("ClipToBounds"));
        Assert.Equal("48", (string?)notificationBody.Attribute("MaxHeight"));
        Assert.Equal("True", (string?)notificationBody.Attribute("ClipToBounds"));
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

        var audioSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "AudioControlsSection");
        var brightnessSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "BrightnessBlock");
        var wifiSection = doc.Descendants(ui + "Border")
            .Single(element => (string?)element.Attribute(xamlName) == "WifiControlsSection");

        Assert.Equal("12", (string?)audioSection.Attribute("Padding"));
        Assert.Equal("8", (string?)audioSection.Attribute("CornerRadius"));
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "OutputDeviceList");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "VolumeSlider");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "AudioSessionMixerSection");
        Assert.Contains(audioSection.Descendants(), element => (string?)element.Attribute(xamlName) == "MicMuteButton");
        Assert.Contains(audioSection.Descendants(ui + "Button"), button =>
            (string?)button.Attribute("Click") == "OutputDevice_Click" &&
            (string?)button.Attribute("HorizontalContentAlignment") == "Stretch");
        Assert.Contains(appDoc.Descendants(ui + "ContentPresenter"), presenter =>
            (string?)presenter.Attribute("HorizontalAlignment") == "{TemplateBinding HorizontalContentAlignment}" &&
            (string?)presenter.Attribute("VerticalAlignment") == "{TemplateBinding VerticalContentAlignment}");
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
