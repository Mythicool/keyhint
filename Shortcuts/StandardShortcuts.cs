namespace KeyHint.Shortcuts;

/// <summary>
/// The shortcuts every Windows app shares, for buttons whose app doesn't publish one.
/// Only names that mean the same thing everywhere; anything ambiguous stays out ("Close" can be a tab or a window,
/// "New" is Ctrl+N in an editor but a menu in Explorer, "Open" is Ctrl+O in an editor but Enter in Explorer).
/// </summary>
public static class StandardShortcuts
{
    static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cut"] = "Ctrl+X",
        ["copy"] = "Ctrl+C",
        ["paste"] = "Ctrl+V",
        ["undo"] = "Ctrl+Z",
        ["redo"] = "Ctrl+Y",
        ["select all"] = "Ctrl+A",
        ["save"] = "Ctrl+S",
        ["save as"] = "Ctrl+Shift+S",
        ["new window"] = "Ctrl+N",
        ["new tab"] = "Ctrl+T",
        ["close tab"] = "Ctrl+W",
        ["print"] = "Ctrl+P",
        ["find"] = "Ctrl+F",
        ["replace"] = "Ctrl+H",
        ["bold"] = "Ctrl+B",
        ["italic"] = "Ctrl+I",
        ["underline"] = "Ctrl+U",
        ["refresh"] = "F5",
        ["reload"] = "F5",
        ["rename"] = "F2",
        ["delete"] = "Delete",
        ["new folder"] = "Ctrl+Shift+N",
        ["zoom in"] = "Ctrl+Plus",
        ["zoom out"] = "Ctrl+Minus",
        ["back"] = "Alt+Left",
        ["forward"] = "Alt+Right",
    };

    // The caption buttons are the same in every window, but only inside a title bar.
    static readonly Dictionary<string, string> TitleBar = new(StringComparer.OrdinalIgnoreCase)
    {
        ["close"] = "Alt+F4",
        ["minimize"] = "Win+Down",
        ["maximize"] = "Win+Up",
        ["restore"] = "Win+Down",
        ["restore down"] = "Win+Down",
    };

    public static IReadOnlyList<Chord>? Lookup(string name, bool inTitleBar)
    {
        string key = Normalize(name);
        if (key.Length == 0) return null;
        if (inTitleBar && TitleBar.TryGetValue(key, out var cap)) return ShortcutText.ParseProperty(cap);
        return ByName.TryGetValue(key, out var text) ? ShortcutText.ParseProperty(text) : null;
    }

    static string Normalize(string name)
    {
        name = ShortcutText.StripEmbedded(name).TrimEnd('.', '…', ' ');
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
