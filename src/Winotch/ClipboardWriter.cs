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
        var options = new ClipboardContentOptions
        {
            IsAllowedInHistory = false,
            IsRoamable = false
        };
        if (!Clipboard.SetContentWithOptions(package, options))
        {
            throw new InvalidOperationException("The clipboard is currently unavailable.");
        }
        Clipboard.Flush();
        return Task.CompletedTask;
    }
}
