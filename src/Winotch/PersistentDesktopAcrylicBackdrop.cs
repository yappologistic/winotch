using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Winotch;

/// <summary>
/// Desktop Acrylic for an always-visible overlay. WinUI's default backdrop
/// configuration follows window activation and deliberately falls back when a
/// window deactivates. Winotch remains visible while another app owns focus, so
/// this material keeps only the input-active policy asserted while preserving
/// the system's theme, transparency, high-contrast, and hardware policies.
/// </summary>
public sealed class PersistentDesktopAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (_controller is not null)
        {
            throw new InvalidOperationException("Persistent acrylic instances cannot be shared between targets.");
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };
        ApplyConfiguration(connectedTarget, xamlRoot);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        ApplyConfiguration(target, xamlRoot);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (_controller is not null)
        {
            _controller.RemoveSystemBackdropTarget(disconnectedTarget);
            _controller.Dispose();
            _controller = null;
        }
    }

    private void ApplyConfiguration(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        if (_controller is null)
        {
            return;
        }

        var configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        configuration.IsInputActive = true;
        _controller.SetSystemBackdropConfiguration(configuration);
    }
}
