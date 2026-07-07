using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Winotch.CommandBar;

public sealed class AppLaunchProvider : ICommandProvider
{
    private IReadOnlyList<AppShortcut>? _shortcuts;

    public string Name => "Apps";
    public int Priority => 100;

    public bool IsEnabled(CommandBarSettings settings) => settings.AppLauncherEnabled;

    public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = Shortcuts()
            .Select(shortcut => (shortcut, score: CommandMatch.Score(query, shortcut.Name)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .Take(6)
            .Select(match => new CommandBarResult(
                match.shortcut.Name,
                string.IsNullOrWhiteSpace(match.shortcut.TargetPath) ? "App" : match.shortcut.TargetPath,
                Name,
                CommandMatch.Rank(match.score, Priority),
                Priority,
                _ =>
                {
                    ShellExecute.Launch(match.shortcut.ShortcutPath);
                    return Task.CompletedTask;
                }))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommandBarResult>>(results);
    }

    private IReadOnlyList<AppShortcut> Shortcuts() =>
        _shortcuts ??= LoadShortcuts();

    private static IReadOnlyList<AppShortcut> LoadShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            .Select(path => new AppShortcut(
                Path.GetFileNameWithoutExtension(path),
                path,
                ShellLinkTargetReader.TryReadTarget(path)))
            .GroupBy(shortcut => shortcut.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(shortcut => shortcut.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private sealed record AppShortcut(string Name, string ShortcutPath, string? TargetPath);
}

internal static class ShellExecute
{
    public static void Launch(string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpFile = path,
            nShow = 5,
            fMask = 0x0000000C
        };
        if (!ShellExecuteEx(ref info))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }
}

internal static class ShellLinkTargetReader
{
    public static string? TryReadTarget(string shortcutPath)
    {
        IShellLinkW? shellLink = null;
        try
        {
            shellLink = (IShellLinkW)(object)new CShellLink();
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var target = new StringBuilder(512);
            shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var text = target.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (shellLink is not null)
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class CShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
