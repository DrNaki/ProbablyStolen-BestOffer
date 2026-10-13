namespace DeepSpaceModSupport;

internal readonly record struct KeyChord(string Key, bool Ctrl, bool Alt, bool Shift)
{
    internal static bool TryParse(string? text, out KeyChord result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, alt = false, shift = false;
        string? key = null;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) return false;
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": if (ctrl) return false; ctrl = true; break;
                case "alt": if (alt) return false; alt = true; break;
                case "shift": if (shift) return false; shift = true; break;
                default: if (key != null) return false; key = part; break;
            }
        }
        if (key == null) return false;
        result = new(key, ctrl, alt, shift); return true;
    }
    public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + Key;
}
