using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyHint.Input;
using KeyHint.Shortcuts;

namespace KeyHint;

public partial class MainWindow : Window
{
    const int MaxHistory = 8;

    static readonly Brush DimBrush = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    static readonly Brush FaintBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    static readonly Brush CapBack = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    static readonly Brush CapEdge = Frozen(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

    readonly Settings settings = Settings.Load();
    readonly MouseHook hook = new();
    readonly Resolver resolver = new();
    readonly bool acceptInjected;
    readonly string? logPath;
    readonly List<Hit> history = [];

    Hit? current;
    int clickSeq, shownSeq;
    bool paused, historyOpen;

    public bool UseStandard => settings.UseStandard;

    public MainWindow(bool acceptInjected, string? logPath)
    {
        InitializeComponent();
        this.acceptInjected = acceptInjected;
        this.logPath = logPath;

        Topmost = settings.Topmost;
        resolver.UseStandard = settings.UseStandard;

        var area = SystemParameters.WorkArea;
        bool onScreen = settings.Left is double l && settings.Top is double t
                        && l > SystemParameters.VirtualScreenLeft - 100 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                        && t > SystemParameters.VirtualScreenTop - 20 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;
        Left = onScreen ? settings.Left!.Value : area.Right - Width - 24;
        Top = onScreen ? settings.Top!.Value : area.Bottom - 150;

        hook.LeftDown += OnLeftDown;
        SizeChanged += OnSizeChanged;

        Loaded += (_, _) =>
        {
            if (!onScreen) Top = area.Bottom - ActualHeight - 24;
            if (settings.HistoryOpen) SetHistoryOpen(true);
            hook.Start();
        };
    }

    // A window you click on must not take the keyboard away from the app you are learning about.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        SetWindowLong(source.Handle, GWL_EXSTYLE, GetWindowLong(source.Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        source.AddHook(static (nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
            if (msg != WM_MOUSEACTIVATE) return 0;
            handled = true;
            return MA_NOACTIVATE;
        });
    }

    public void SetTopmost(bool on)
    {
        Topmost = on;
        settings.Topmost = on;
        SaveSettings();
    }

    public void SetUseStandard(bool on)
    {
        settings.UseStandard = on;
        resolver.UseStandard = on;
        SaveSettings();
    }

    public void SetPaused(bool on)
    {
        paused = on;
        MissText.Visibility = Visibility.Collapsed;
        if (on)
        {
            ItemText.Text = "Paused";
            AppText.Text = "Untick Pause in the tray menu to resume";
            KeysPanel.Visibility = NoteText.Visibility = Visibility.Collapsed;
        }
        else if (current != null) ShowHit(current, flash: false);
        else ShowIdle();
    }

    public void Shutdown()
    {
        SaveSettings();
        hook.Dispose();
    }

    void SaveSettings()
    {
        settings.Left = Left;
        settings.Top = Top;
        settings.HistoryOpen = historyOpen;
        settings.Save();
    }

    // ---- clicks ----

    void OnLeftDown(ClickEvent click)
    {
        if (paused || !IsVisible) return;
        if (click.Injected && !acceptInjected) return;   // only a person's own clicks teach anything
        if (OverSelf(click.X, click.Y)) return;

        int seq = ++clickSeq;
        var trace = logPath != null ? new StringWriter() : null;
        Task.Run(() => resolver.Resolve(click.X, click.Y, trace))
            .ContinueWith(t => Dispatcher.BeginInvoke(() => Present(seq, t.IsCompletedSuccessfully ? t.Result : null, trace?.ToString())));
    }

    bool OverSelf(int x, int y)
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != 0 && GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    void Present(int seq, Resolution? result, string? trace)
    {
        if (seq < shownSeq) return;   // a later click has already been answered
        shownSeq = seq;
        if (result == null) return;
        Log(result, trace);
        if (paused) return;

        if (result.Hit is { } hit)
        {
            MissText.Visibility = Visibility.Collapsed;
            current = hit;
            ShowHit(hit, flash: true);
            AddHistory(hit);
        }
        else
        {
            MissText.Text = $"Clicked “{result.Item}”: no shortcut";
            MissText.Visibility = Visibility.Visible;
        }
    }

    void Log(Resolution r, string? trace)
    {
        if (logPath == null) return;
        try
        {
            File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}\t{r.App}\t{r.Item}\t{r.Hit?.Keys ?? "(none)"}\t{r.Hit?.Source.ToString() ?? ""}\n{trace}");
        }
        catch { }
    }

    // ---- what the card shows ----

    void ShowIdle()
    {
        ItemText.Text = "Click a menu or button";
        AppText.Text = "Its keyboard shortcut will show up here";
        KeysPanel.Visibility = NoteText.Visibility = Visibility.Collapsed;
    }

    void ShowHit(Hit hit, bool flash)
    {
        ItemText.Text = hit.Item;
        AppText.Text = hit.App;

        KeysPanel.Children.Clear();
        foreach (var element in BuildKeys(hit.Chords, 20)) KeysPanel.Children.Add(element);
        KeysPanel.Visibility = Visibility.Visible;

        NoteText.Text = hit.Source switch
        {
            HitSource.Standard => "This app didn't list one. This is the usual Windows shortcut for it.",
            HitSource.AccessKey => "A menu key: press Alt, then the letter.",
            _ => "",
        };
        NoteText.Visibility = NoteText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!flash) return;
        var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        KeysScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        KeysScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        KeysPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(160)));
    }

    static IEnumerable<UIElement> BuildKeys(IReadOnlyList<Chord> chords, double size)
    {
        for (int c = 0; c < chords.Count; c++)
        {
            if (c > 0) yield return Joiner("then", size * 0.6);
            for (int k = 0; k < chords[c].Keys.Count; k++)
            {
                if (k > 0) yield return Joiner("+", size * 0.7);
                yield return Cap(chords[c].Keys[k], size);
            }
        }
    }

    static UIElement Cap(string label, double size) => new Border
    {
        CornerRadius = new CornerRadius(Math.Max(4, size * 0.3)),
        Background = CapBack,
        BorderBrush = CapEdge,
        BorderThickness = new Thickness(1, 1, 1, Math.Max(2, size * 0.15)),   // the thicker bottom edge makes it read as a key
        MinWidth = size * 1.9,
        Padding = new Thickness(size * 0.5, size * 0.12, size * 0.5, size * 0.18),
        Margin = new Thickness(0, 2, 0, 2),
        Child = new TextBlock
        {
            Text = label,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    static UIElement Joiner(string text, double size) => new TextBlock
    {
        Text = text,
        FontSize = size,
        Foreground = FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
    };

    // ---- history ----

    void AddHistory(Hit hit)
    {
        history.RemoveAll(h => h.Item == hit.Item && h.App == hit.App && h.Keys == hit.Keys);
        history.Insert(0, hit);
        if (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
        if (historyOpen) RebuildHistory();
    }

    void RebuildHistory()
    {
        HistoryPanel.Children.Clear();
        if (history.Count == 0)
        {
            HistoryPanel.Children.Add(new TextBlock { Text = "Nothing yet", FontSize = 12, Foreground = FaintBrush });
            return;
        }

        foreach (var hit in history)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2), ToolTip = hit.App };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = hit.Item,
                FontSize = 13,
                Foreground = DimBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            var keys = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var element in BuildKeys(hit.Chords, 12)) keys.Children.Add(element);
            Grid.SetColumn(keys, 1);
            row.Children.Add(keys);

            HistoryPanel.Children.Add(row);
        }
    }

    void SetHistoryOpen(bool on)
    {
        historyOpen = on;
        HistoryPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = on ? "" : "";
        HistoryButton.ToolTip = on ? "Hide the list" : "Show the last few shortcuts";
        if (on) RebuildHistory();
    }

    // ---- window ----

    // The card grows when a shortcut appears or the list opens. In the lower half of a screen it grows upward, so it never runs off the bottom.
    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || !e.HeightChanged || e.PreviousSize.Height <= 0) return;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double areaTop = screen.Top / scale, areaBottom = screen.Bottom / scale;

        bool lowerHalf = Top + e.PreviousSize.Height / 2 > (areaTop + areaBottom) / 2;
        if (lowerHalf) Top += e.PreviousSize.Height - e.NewSize.Height;
        Top = Math.Max(areaTop, Math.Min(Top, areaBottom - e.NewSize.Height));
    }

    void History_Click(object sender, RoutedEventArgs e)
    {
        SetHistoryOpen(!historyOpen);
        SaveSettings();
    }

    void Hide_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Hide();
    }

    void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
        SaveSettings();
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    static extern int SetWindowLong(nint hwnd, int index, int value);
}
