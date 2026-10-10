using System.IO;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyHint;

public partial class App : Application
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    Mutex? mutex;
    Forms.NotifyIcon? tray;
    MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args;
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            Shutdown(Diagnostics.SelfTest(args[1]));
            return;
        }
        if (args.Length >= 3 && args[0] == "--survey")
        {
            Diagnostics.Survey(args[1], args[2]);
            Shutdown(0);
            return;
        }
        if (args.Length >= 4 && args[0] == "--inspect")
        {
            Diagnostics.Inspect(int.Parse(args[1]), int.Parse(args[2]), args[3]);
            Shutdown(0);
            return;
        }

        mutex = new Mutex(true, "KeyHint.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, a) =>
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), $"{DateTime.Now:u} {a.Exception}\n\n");
            }
            catch { }
            a.Handled = true;
        };

        // --accept-injected counts clicks made by other programs (for testing); --log appends each lookup to a file.
        bool acceptInjected = args.Contains("--accept-injected");
        int logAt = Array.IndexOf(args, "--log");
        string? logPath = logAt >= 0 && logAt + 1 < args.Length ? args[logAt + 1] : null;

        window = new MainWindow(acceptInjected, logPath);
        window.Show();
        CreateTray();
    }

    void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => ToggleWindow());

        var paused = new Forms.ToolStripMenuItem("Pause") { CheckOnClick = true };
        paused.CheckedChanged += (_, _) => window!.SetPaused(paused.Checked);
        menu.Items.Add(paused);

        var onTop = new Forms.ToolStripMenuItem("Always on top") { Checked = window!.Topmost, CheckOnClick = true };
        onTop.CheckedChanged += (_, _) => window.SetTopmost(onTop.Checked);
        menu.Items.Add(onTop);

        var standard = new Forms.ToolStripMenuItem("Guess usual Windows shortcuts") { Checked = window.UseStandard, CheckOnClick = true };
        standard.ToolTipText = "When an app doesn't list a shortcut for a button like Copy or Save, show the one Windows apps usually share";
        standard.CheckedChanged += (_, _) => window.SetUseStandard(standard.Checked);
        menu.Items.Add(standard);

        var autostart = new Forms.ToolStripMenuItem("Start with Windows") { Checked = IsAutostart(), CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);
        menu.Items.Add(autostart);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());

        tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "KeyHint", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, a) =>
        {
            if (a.Button == Forms.MouseButtons.Left) ToggleWindow();
        };
    }

    void ToggleWindow()
    {
        if (window!.IsVisible) window.Hide();
        else window.Show();
    }

    void Quit()
    {
        window?.Shutdown();
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        Shutdown(0);
    }

    static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("KeyHint") != null;
    }

    static void SetAutostart(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("KeyHint", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("KeyHint", false);
    }

    static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var back = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(20, 184, 166));
            using var key = new System.Drawing.Drawing2D.GraphicsPath();
            const int r = 9;
            key.AddArc(1, 1, r * 2, r * 2, 180, 90);
            key.AddArc(31 - r * 2, 1, r * 2, r * 2, 270, 90);
            key.AddArc(31 - r * 2, 31 - r * 2, r * 2, r * 2, 0, 90);
            key.AddArc(1, 31 - r * 2, r * 2, r * 2, 90, 90);
            key.CloseFigure();
            g.FillPath(back, key);
            using var font = new System.Drawing.Font("Segoe UI", 18, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var format = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
            g.DrawString("K", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, 32, 33), format);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
