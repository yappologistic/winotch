using System.Windows.Media;

namespace Winotch;

public sealed record ShelfTileViewModel(
    string FullPath,
    string DisplayName,
    bool Exists,
    ImageSource? Icon);
