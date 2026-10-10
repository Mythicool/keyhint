using System.Text;
using System.Text.RegularExpressions;

namespace KeyHint.Shortcuts;

/// <summary>One key combination, as the labels to draw on keycaps: ["Ctrl", "Shift", "S"].</summary>
public sealed record Chord(IReadOnlyList<string> Keys)
{
    public override string ToString() => string.Join("+", Keys);
}

/// <summary>Turns the shortcut text apps hand out ("Ctrl+S", "Control+Shift+Esc", "Ctrl+K, Ctrl+C") into keycap labels.</summary>
public static partial class ShortcutText
{
    static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["control"] = "Ctrl", ["ctrl"] = "Ctrl", ["ctl"] = "Ctrl",
        ["alt"] = "Alt", ["shift"] = "Shift",
        ["win"] = "Win", ["windows"] = "Win", ["windows key"] = "Win", ["cmd"] = "Win",
        ["escape"] = "Esc", ["esc"] = "Esc",
        ["delete"] = "Del", ["del"] = "Del",
        ["insert"] = "Ins", ["ins"] = "Ins",
        ["return"] = "Enter", ["enter"] = "Enter",
        ["space"] = "Space", ["spacebar"] = "Space", ["space bar"] = "Space",
        ["backspace"] = "Backspace", ["back"] = "Backspace",
        ["tab"] = "Tab", ["home"] = "Home", ["end"] = "End",
        ["pageup"] = "PgUp", ["page up"] = "PgUp", ["pgup"] = "PgUp", ["prior"] = "PgUp",
        ["pagedown"] = "PgDn", ["page down"] = "PgDn", ["pgdn"] = "PgDn", ["next"] = "PgDn",
        ["left"] = "←", ["left arrow"] = "←", ["leftarrow"] = "←",
        ["right"] = "→", ["right arrow"] = "→", ["rightarrow"] = "→",
        ["up"] = "↑", ["up arrow"] = "↑", ["uparrow"] = "↑",
        ["down"] = "↓", ["down arrow"] = "↓", ["downarrow"] = "↓",
        ["plus"] = "+", ["add"] = "+",
        ["minus"] = "−", ["subtract"] = "−", ["-"] = "−",
        ["equals"] = "=", ["equal"] = "=",
        ["comma"] = ",", ["period"] = ".", ["dot"] = ".",
        ["printscreen"] = "PrtSc", ["print screen"] = "PrtSc", ["prtsc"] = "PrtSc",
        ["capslock"] = "Caps", ["caps lock"] = "Caps",
        ["menu"] = "Menu", ["apps"] = "Menu",
    };

    static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
        { "Ctrl", "Alt", "Shift", "Win" };

    // A real combination written inside a label or tooltip: it must start with a modifier, so "Press F5 to refresh" never matches.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:(?:Ctrl|Control|Alt|Shift|Win|Windows)\s?\+\s?)+(?:F\d{1,2}|[A-Za-z0-9]|Plus|Minus|Equals|Delete|Del|Escape|Esc|Enter|Return|Tab|Space(?:bar)?|Backspace|Home|End|Insert|Ins|Page\s?Up|Page\s?Down|PgUp|PgDn|(?:Left|Right|Up|Down)(?:\s?Arrow)?|[+\-=,./;'\[\]\\`])(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedChord();

    [GeneratedRegex(@"\s*[\(\[]\s*$")]
    private static partial Regex DanglingBracket();

    /// <summary>The text of a property that is, by definition, a shortcut (UI Automation's AcceleratorKey / AccessKey).</summary>
    public static IReadOnlyList<Chord>? ParseProperty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        // "Ctrl+Y or Ctrl+Shift+Z" and "Ctrl+Y; Ctrl+Shift+Z" are alternatives: the first one is enough.
        int alt = text.IndexOf(" or ", StringComparison.OrdinalIgnoreCase);
        if (alt > 0) text = text[..alt];
        alt = text.IndexOf(';');
        if (alt > 0) text = text[..alt];

        // "Ctrl+K, Ctrl+C" and "Alt+F, S" are one chord after another.
        var pieces = Regex.Split(text, @",\s+|\s{2,}").ToList();

        // Some apps spell Alt+F as "Alt, F": press Alt, then F.
        if (pieces.Count > 1 && OnlyModifiers(pieces[0]))
        {
            pieces[1] = pieces[0] + "+" + pieces[1];
            pieces.RemoveAt(0);
        }

        var chords = new List<Chord>();
        foreach (var piece in pieces)
        {
            var chord = ParseChord(piece);
            if (chord == null) return null;
            chords.Add(chord);
        }
        return chords.Count is > 0 and <= 3 ? chords : null;
    }

    /// <summary>A shortcut written into a label or tooltip: "Bold (Ctrl+B)", "Save&#9;Ctrl+S".</summary>
    public static IReadOnlyList<Chord>? FindEmbedded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = EmbeddedChord().Match(text);
        if (!m.Success) return null;
        var chord = ParseChord(m.Value);
        return chord == null ? null : [chord];
    }

    /// <summary>The label with any written-in shortcut taken out: "Bold (Ctrl+B)" becomes "Bold".</summary>
    public static string StripEmbedded(string text)
    {
        int tab = text.IndexOf('\t');
        if (tab >= 0) text = text[..tab];
        var m = EmbeddedChord().Match(text);
        if (m.Success)
        {
            string before = DanglingBracket().Replace(text[..m.Index], "");
            string after = text[(m.Index + m.Length)..].TrimStart(')', ']', ' ');
            text = (before + " " + after).Trim();
        }
        return text.Replace("&", "").Trim();
    }

    static Chord? ParseChord(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        // "Ctrl++" is Ctrl and the plus key.
        if (text == "+") text = "Plus";
        else if (text.EndsWith("++")) text = text[..^2] + "+Plus";

        var keys = new List<string>();
        foreach (var raw in text.Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            if (part.Length > 14) return null;
            keys.Add(Label(part));
        }
        if (keys.Count is 0 or > 5) return null;

        // Modifiers lead; a chord of only modifiers ("Ctrl+Shift") is not a shortcut worth showing.
        if (keys.All(k => Modifiers.Contains(k))) return null;
        return new Chord(keys);
    }

    static bool OnlyModifiers(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(p => Modifiers.Contains(Label(p)));
    }

    static string Label(string part)
    {
        if (Labels.TryGetValue(part, out var label)) return label;
        if (Regex.IsMatch(part, @"^[fF]\d{1,2}$")) return part.ToUpperInvariant();
        if (part.Length == 1) return part.ToUpperInvariant();
        var sb = new StringBuilder(part.ToLowerInvariant());
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    public static string Describe(IReadOnlyList<Chord> chords) => string.Join(", ", chords.Select(c => c.ToString()));
}
