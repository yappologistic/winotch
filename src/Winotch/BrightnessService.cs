namespace Winotch;

public sealed class BrightnessService
{
    public Task<IReadOnlyList<BrightnessDisplay>> GetDisplaysAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<BrightnessDisplay>>(() =>
        {
            var displays = new List<BrightnessDisplay>();
            displays.AddRange(WmiBrightness.ReadDisplays());
            displays.AddRange(DdcBrightness.ReadDisplays());
            return BrightnessDisplaySelection.PreferControllableDisplays(displays);
        }, cancellationToken);

    public Task SetBrightnessAsync(BrightnessDisplay display, int percent, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var value = BrightnessMath.FromPercent(display.Minimum, display.Maximum, percent);
            if (display.Kind == BrightnessDisplayKind.Internal)
            {
                WmiBrightness.SetBrightness(display.Id, value);
            }
            else
            {
                DdcBrightness.SetBrightness(display.Id, value);
            }
        }, cancellationToken);
}
