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
        Assert.Contains("AutomationProperties.Name=\"Toast duration scale\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"ICS subscription URLs\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Request notification access\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("Click=\"RequestNotificationAccessClick\"", xaml);
        Assert.DoesNotContain("DropShadowEffect", xaml);
    }

    [Fact]
    public void ProductDocsDescribeMiniOnlyForegroundAndPrivacySurfaces()
    {
        var readme = ReadRepoFile("README.md");
        var architecture = ReadRepoFile("docs", "architecture.md");

        Assert.Contains("Foreground detection currently keeps the shell in Mini for every foreground app state", readme);
        Assert.Contains("Winotch requests notification history access only when the user clicks Request access in Settings", readme);
        Assert.Contains("Clipboard history stays in memory", readme);
        Assert.Contains("`ForegroundWindowService.DecideMode` currently returns `Mini` for every foreground app state", architecture);
        Assert.Contains("Settings owns the explicit Request access button", architecture);
        Assert.Contains("NotificationService.RequestHistoryAccessAsync", architecture);
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
