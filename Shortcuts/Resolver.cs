using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace KeyHint.Shortcuts;

public enum HitSource
{
    /// <summary>The app itself published it (an accelerator, or written into the label or tooltip).</summary>
    App,
    /// <summary>The app published none; this is the shortcut Windows apps share for that action.</summary>
    Standard,
    /// <summary>A menu's Alt+letter key.</summary>
    AccessKey,
}

public sealed record Hit(string Item, string App, IReadOnlyList<Chord> Chords, HitSource Source)
{
    public string Keys => ShortcutText.Describe(Chords);
}

/// <summary>What was under the click. <see cref="Hit"/> is null when that item has no shortcut.</summary>
public sealed record Resolution(string Item, string App, Hit? Hit);

/// <summary>Finds the shortcut of whatever is at a screen position, by asking the app through UI Automation.</summary>
public sealed class Resolver
{
    sealed record Node(ControlType Type, string Name, string Accel, string Access, string Help, string AutoId, string ClassName, string TypeName, int Pid);

    // A click on a button's text or icon lands on a child; those are parts of the control, so keep climbing past them.
    static readonly HashSet<ControlType> Parts = [ControlType.Text, ControlType.Image, ControlType.Custom, ControlType.Group, ControlType.Pane];

    // Climbing never goes out through a container, or the whole toolbar's shortcut would be pinned on one click.
    static readonly HashSet<ControlType> Containers =
    [
        ControlType.Window, ControlType.Document, ControlType.ToolBar, ControlType.Menu, ControlType.MenuBar, ControlType.List,
        ControlType.Tree, ControlType.Tab, ControlType.Table, ControlType.TitleBar, ControlType.DataGrid, ControlType.StatusBar,
    ];

    static readonly HashSet<ControlType> Actions = [ControlType.Button, ControlType.SplitButton, ControlType.MenuItem, ControlType.Hyperlink];

    static readonly ConcurrentDictionary<int, string> AppNames = new();

    const int MaxParents = 4;

    public bool UseStandard { get; set; } = true;

    public Resolution? Resolve(int x, int y, TextWriter? trace = null)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            return element == null ? null : Resolve(element, trace);
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return null;
        }
    }

    /// <summary>With <paramref name="trace"/>, writes every property it looked at (for --inspect).</summary>
    public Resolution? Resolve(AutomationElement element, TextWriter? trace = null)
    {
        try
        {
            var chain = Climb(element);
            if (chain.Count == 0) return null;
            if (chain[0].Pid == Environment.ProcessId) return null;

            string app = AppName(chain[0].Pid);
            if (trace != null) Dump(trace, chain, app);

            for (int i = 0; i < chain.Count; i++)
            {
                var node = chain[i];
                if (i > 0 && Containers.Contains(node.Type)) break;

                var hit = Evaluate(node, i + 1 < chain.Count ? chain[i + 1] : null, app);
                if (hit != null) return new Resolution(hit.Item, app, hit);

                // A real control with no shortcut ends the search; only decoration inside it means "keep looking up".
                if (!Parts.Contains(node.Type)) break;
            }

            string item = chain.Select(DisplayName).FirstOrDefault(n => n.Length > 0) ?? chain[0].TypeName;
            return new Resolution(item, app, null);
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return null;
        }
    }

    // The window closed or hung between the click and the lookup.
    static bool IsGone(Exception ex) =>
        ex is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException or TimeoutException;

    Hit? Evaluate(Node n, Node? parent, string app)
    {
        string item = DisplayName(n);
        if (item.Length == 0) item = n.TypeName;

        // 1. What the app says.
        if (ShortcutText.ParseProperty(n.Accel) is { } accel) return new Hit(item, app, accel, HitSource.App);

        // 2. A shortcut written into the label or tooltip, like "Bold (Ctrl+B)".
        if ((ShortcutText.FindEmbedded(n.Name) ?? ShortcutText.FindEmbedded(n.Help)) is { } written)
            return new Hit(item, app, written, HitSource.App);

        // 3. The usual Windows one, for plain actions the app left unlabeled.
        if (UseStandard && Actions.Contains(n.Type)
            && StandardShortcuts.Lookup(n.Name, inTitleBar: parent?.Type == ControlType.TitleBar) is { } usual)
            return new Hit(item, app, usual, HitSource.Standard);

        // 4. A menu's Alt key. A bare letter only works while that menu is already open, so it needs the Alt to count.
        if (ShortcutText.ParseProperty(n.Access) is { } access && access[0].Keys.Contains("Alt"))
            return new Hit(item, app, access, HitSource.AccessKey);

        return null;
    }

    static string DisplayName(Node n)
    {
        string name = ShortcutText.StripEmbedded(n.Name);
        return name.Length > 0 ? name : (n.AutoId.Length is > 0 and < 24 ? n.AutoId : "");
    }

    static List<Node> Climb(AutomationElement element)
    {
        var chain = new List<Node>();
        var cache = NewCache();
        var current = element.GetUpdatedCache(cache);
        chain.Add(Read(current));

        var walker = TreeWalker.ControlViewWalker;
        for (int i = 0; i < MaxParents && current != null; i++)
        {
            // No need to go further once we're past the parts of a control and into containers.
            if (i > 0 && !Parts.Contains(chain[^1].Type) && !Actions.Contains(chain[^1].Type) && chain[^1].Type != ControlType.TitleBar) break;
            current = walker.GetParent(current, cache);
            if (current == null) break;
            chain.Add(Read(current));
        }
        return chain;
    }

    static CacheRequest NewCache()
    {
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        foreach (var p in new[]
        {
            AutomationElement.NameProperty, AutomationElement.AcceleratorKeyProperty, AutomationElement.AccessKeyProperty,
            AutomationElement.HelpTextProperty, AutomationElement.ControlTypeProperty, AutomationElement.AutomationIdProperty,
            AutomationElement.ClassNameProperty, AutomationElement.LocalizedControlTypeProperty, AutomationElement.ProcessIdProperty,
        })
            cache.Add(p);
        return cache;
    }

    static Node Read(AutomationElement e)
    {
        var c = e.Cached;
        return new Node(c.ControlType, c.Name ?? "", c.AcceleratorKey ?? "", c.AccessKey ?? "", c.HelpText ?? "",
            c.AutomationId ?? "", c.ClassName ?? "", c.LocalizedControlType ?? "", c.ProcessId);
    }

    static string AppName(int pid) => AppNames.GetOrAdd(pid, static id =>
    {
        try
        {
            using var p = Process.GetProcessById(id);
            string name = p.ProcessName;
            try
            {
                string? description = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
            catch { }   // an elevated app won't say; its file name is fine
            return name;
        }
        catch
        {
            return "";
        }
    });

    static void Dump(TextWriter w, List<Node> chain, string app)
    {
        w.WriteLine($"app: {app}");
        for (int i = 0; i < chain.Count; i++)
        {
            var n = chain[i];
            w.WriteLine($"[{i}] {n.Type.ProgrammaticName.Replace("ControlType.", "")}  name='{n.Name.Replace("\t", "\\t")}'  id='{n.AutoId}'  class='{n.ClassName}'");
            w.WriteLine($"     accelerator='{n.Accel}'  access='{n.Access}'  help='{n.Help}'");
        }
    }
}
