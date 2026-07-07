namespace Winotch;

public static class ShelfLaunchTargets
{
    public static IReadOnlyList<string> For(ShelfItem item) => item.Kind switch
    {
        ShelfItemKind.Files => item.FilePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
        ShelfItemKind.Link when !string.IsNullOrWhiteSpace(item.Text) => [item.Text],
        _ => []
    };
}
