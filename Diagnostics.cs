using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using KeyHint.Shortcuts;

namespace KeyHint;

/// <summary>Checks that run without showing the window.</summary>
public static class Diagnostics
{
    /// <summary>Feeds the parser the shapes of shortcut text real apps produce. Returns 0 when all pass.</summary>
    public static int SelfTest(string outPath)
    {
        var lines = new List<string>();
        int failed = 0;

        void Property(string input, string? expected)
        {
            string? got = ShortcutText.ParseProperty(input) is { } c ? ShortcutText.Describe(c) : null;
            Record($"property '{input.Replace("\t", "\\t")}'", got, expected);
        }
        void Embedded(string input, string? expected)
        {
            string? got = ShortcutText.FindEmbedded(input) is { } c ? ShortcutText.Describe(c) : null;
            Record($"embedded '{input.Replace("\t", "\\t")}'", got, expected);
        }
        void Strip(string input, string expected) => Record($"strip '{input.Replace("\t", "\\t")}'", ShortcutText.StripEmbedded(input), expected);
        void Standard(string name, bool titleBar, string? expected)
        {
            string? got = StandardShortcuts.Lookup(name, titleBar) is { } c ? ShortcutText.Describe(c) : null;
            Record($"standard '{name}'{(titleBar ? " (title bar)" : "")}", got, expected);
        }
        void Record(string what, string? got, string? expected)
        {
            bool ok = got == expected;
            if (!ok) failed++;
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {what} -> {got ?? "(none)"}{(ok ? "" : $"   expected {expected ?? "(none)"}")}");
        }

        Property("Ctrl+S", "Ctrl+S");
        Property("ctrl+shift+s", "Ctrl+Shift+S");
        Property("Control+Shift+Esc", "Ctrl+Shift+Esc");
        Property("F5", "F5");
        Property("Alt+F4", "Alt+F4");
        Property("Ctrl++", "Ctrl++");
        Property("Ctrl+Plus", "Ctrl++");
        Property("Ctrl+-", "Ctrl+−");
        Property("Ctrl+,", "Ctrl+,");
        Property("Alt+Left Arrow", "Alt+←");
        Property("Ctrl+K, Ctrl+C", "Ctrl+K, Ctrl+C");
        Property("Alt+f, s", "Alt+F, S");
        Property("Alt, F", "Alt+F");
        Property("Alt, H, B", "Alt+H, B");
        Property("Alt", null);
        Property("Ctrl+Y or Ctrl+Shift+Z", "Ctrl+Y");
        Property("Delete", "Del");
        Property("Ctrl+Alt+Shift+Win+F", "Ctrl+Alt+Shift+Win+F");
        Property("", null);
        Property("Ctrl+Shift", null);
        Property("This is a long sentence that is not a shortcut at all", null);

        Embedded("Bold (Ctrl+B)", "Ctrl+B");
        Embedded("Save\tCtrl+S", "Ctrl+S");
        Embedded("Back (Alt+Left Arrow)", "Alt+←");
        Embedded("Zoom in (Ctrl + Plus)", "Ctrl++");
        Embedded("Task Manager Ctrl+Shift+Esc", "Ctrl+Shift+Esc");
        Embedded("Press F5 to refresh", null);
        Embedded("Shift happens", null);
        Embedded("Ctrl+Shiftless", null);
        Embedded("", null);

        Strip("Bold (Ctrl+B)", "Bold");
        Strip("Save\tCtrl+S", "Save");
        Strip("&File", "File");
        Strip("Back (Alt+Left Arrow) button", "Back button");
        Strip("Plain name", "Plain name");

        Standard("Copy", false, "Ctrl+C");
        Standard("Save as...", false, "Ctrl+Shift+S");
        Standard("  select   all ", false, "Ctrl+A");
        Standard("New", false, null);
        Standard("Open", false, null);
        Standard("Close", false, null);
        Standard("Close", true, "Alt+F4");
        Standard("Minimize", true, "Win+↓");
        Standard("Whatever", false, null);

        lines.Add(failed == 0 ? "ALL PASS" : $"{failed} FAILED");
        File.WriteAllLines(outPath, lines);
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Lists every button, menu item and tab of a running app's windows, with what KeyHint would show for each.</summary>
    public static void Survey(string processName, string outPath)
    {
        var pids = Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();
        var resolver = new Resolver();
        var wanted = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

        var lines = new List<string>();
        int total = 0, withShortcut = 0;
        foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                if (!pids.Contains(top.Current.ProcessId)) continue;
                lines.Add($"# window: {top.Current.Name}");
                foreach (AutomationElement item in top.FindAll(TreeScope.Descendants, wanted))
                {
                    if (total >= 400) break;
                    var r = resolver.Resolve(item);
                    if (r == null) continue;
                    total++;
                    if (r.Hit != null) withShortcut++;
                    lines.Add($"{(r.Hit != null ? "*" : " ")} {r.Item}\t{r.Hit?.Keys ?? "(none)"}\t{r.Hit?.Source.ToString() ?? ""}");
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException) { }
        }
        lines.Add($"# {total} items looked at, {withShortcut} with a shortcut");
        File.WriteAllLines(outPath, lines);
    }

    /// <summary>Writes what Windows reports about whatever is at a screen position (physical pixels).</summary>
    public static void Inspect(int x, int y, string outPath)
    {
        using var w = new StringWriter();
        var resolver = new Resolver();
        var result = resolver.Resolve(x, y, w);
        w.WriteLine();
        if (result == null) w.WriteLine("result: nothing found");
        else if (result.Hit == null) w.WriteLine($"result: '{result.Item}' in {result.App} has no shortcut");
        else w.WriteLine($"result: '{result.Hit.Item}' in {result.App} -> {result.Hit.Keys} ({result.Hit.Source})");
        File.WriteAllText(outPath, w.ToString());
    }
}
