using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyHint.Input;

public readonly record struct ClickEvent(int X, int Y, bool Injected);

/// <summary>
/// Hears every left mouse press on the desktop. The callback runs on the UI thread and holds up all mouse input until it
/// returns, so it only passes the position on; the slow lookup happens elsewhere.
/// </summary>
public sealed class MouseHook : IDisposable
{
    const int WH_MOUSE_LL = 14;
    const int WM_LBUTTONDOWN = 0x0201;
    const uint LLMHF_INJECTED = 0x01;
    const uint LLMHF_LOWER_IL_INJECTED = 0x02;

    delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    readonly HookProc proc;   // a field, so the garbage collector can't free the callback while Windows holds it
    nint handle;

    public event Action<ClickEvent>? LeftDown;

    public MouseHook() => proc = Callback;

    public void Start()
    {
        if (handle != 0) return;
        string module = Process.GetCurrentProcess().MainModule!.ModuleName;
        handle = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(module), 0);
        if (handle == 0) throw new InvalidOperationException($"Couldn't listen to the mouse (error {Marshal.GetLastWin32Error()})");
    }

    nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == WM_LBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            bool injected = (data.Flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
            try { LeftDown?.Invoke(new ClickEvent(data.X, data.Y, injected)); }
            catch { }
        }
        return CallNextHookEx(handle, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (handle == 0) return;
        UnhookWindowsHookEx(handle);
        handle = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern nint GetModuleHandle(string? name);
}
