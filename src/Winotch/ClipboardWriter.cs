using Windows.ApplicationModel.DataTransfer;

namespace Winotch;

internal static class ClipboardWriter
{
    public static Task WriteTextAsync(string text)
    {
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        package.SetText(text);
        package.Properties.Add("ExcludeClipboardContentFromMonitorProcessing", true);
        package.Properties.ExcludeFromHistory = true;
        package.Properties.ExcludeFromRoaming = true;
        Clipboard.SetContent(package);
        Clipboard.Flush();
        return Task.CompletedTask;
    }
}
