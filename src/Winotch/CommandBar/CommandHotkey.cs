namespace Winotch.CommandBar;

public readonly record struct CommandHotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint NoRepeat = 0x4000;

    public uint RegisterModifiers => Modifiers | NoRepeat;
}

public static class CommandHotkeyParser
{
    private static readonly IReadOnlyDictionary<string, uint> NamedKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D
    };

    public static bool TryParse(string text, out CommandHotkey hotkey)
    {
        hotkey = default;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        uint modifiers = 0;
        uint key = 0;
        foreach (var part in parts)
        {
            if (TryParseModifier(part, ref modifiers))
            {
                continue;
            }

            if (key != 0 || !TryParseKey(part, out key))
            {
                return false;
            }
        }

        if (modifiers == 0 || key == 0)
        {
            return false;
        }

        hotkey = new CommandHotkey(modifiers, key);
        return true;
    }

    private static bool TryParseModifier(string part, ref uint modifiers)
    {
        var flag = part switch
        {
            "Ctrl" or "Control" => CommandHotkey.ModControl,
            "Alt" => CommandHotkey.ModAlt,
            "Shift" => CommandHotkey.ModShift,
            "Win" or "Windows" => CommandHotkey.ModWin,
            _ => 0u
        };
        if (flag == 0 || (modifiers & flag) != 0)
        {
            return false;
        }

        modifiers |= flag;
        return true;
    }

    private static bool TryParseKey(string part, out uint key)
    {
        if (NamedKeys.TryGetValue(part, out key))
        {
            return true;
        }

        if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
        {
            key = char.ToUpperInvariant(part[0]);
            return true;
        }

        if (part.Length is 2 or 3 &&
            part[0] is 'F' or 'f' &&
            int.TryParse(part[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key = (uint)(0x70 + functionKey - 1);
            return true;
        }

        key = 0;
        return false;
    }
}
