using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Komorebi.Core;

namespace Komorebi.Desktop;
internal record LiveWindow(nint Handle, string Title, string Executable)
{
    public override string ToString() => $"{Path.GetFileNameWithoutExtension(Executable)} — {Title}";
}
internal static class NativeWindows
{
    private delegate bool EnumProc(nint handle, nint data);
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Placement
    {
        public int Length, Flags, Show;
        public Point Min, Max;
        public Rect Normal;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    public static List<LiveWindow> List()
    {
        var result = new List<LiveWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != 0) return true;
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(1024);
            GetWindowText(handle, title, title.Capacity);
            if (title.Length == 0) return true;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == Environment.ProcessId) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                string? executable = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(executable)) result.Add(new(handle, title.ToString(), executable));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { }
            return true;
        }, 0);
        return result;
    }
    public static Screen Monitor(string device) => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    public static WindowSlot Capture(LiveWindow window)
    {
        var screen = Screen.FromHandle(window.Handle);
        var area = screen.WorkingArea;
        if (!GetWindowRect(window.Handle, out var rect)) throw new InvalidOperationException("Fenster ist nicht mehr verfügbar.");
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        // Normal placement uses workspace coordinates; convert back to screen coordinates.
        if (GetWindowPlacement(window.Handle, ref placement) && (placement.Show == 2 || IsZoomed(window.Handle)))
        {
            rect = placement.Normal;
            rect.Left += area.Left - screen.Bounds.Left;
            rect.Right += area.Left - screen.Bounds.Left;
            rect.Top += area.Top - screen.Bounds.Top;
            rect.Bottom += area.Top - screen.Bounds.Top;
        }
        return new WindowSlot
        {
            Executable = window.Executable, Title = window.Title, Monitor = screen.DeviceName,
            Maximized = IsZoomed(window.Handle),
            Bounds = Layouts.Clamp(new((double)(rect.Left - area.Left) / area.Width, (double)(rect.Top - area.Top) / area.Height,
                (double)(rect.Right - rect.Left) / area.Width, (double)(rect.Bottom - rect.Top) / area.Height))
        };
    }
    public static void Move(LiveWindow window, string monitor, Box bounds, bool maximize = false)
    {
        // Revalidate the handle before touching it: Windows may recycle HWND values.
        if (!List().Any(w => w.Handle == window.Handle && w.Executable == window.Executable))
            throw new InvalidOperationException("Fenster wurde geschlossen oder ist nicht zugänglich.");
        var area = Monitor(monitor).WorkingArea;
        var box = Layouts.Clamp(bounds);
        ShowWindow(window.Handle, 9);
        if (!SetWindowPos(window.Handle, 0, area.Left + (int)(box.X * area.Width), area.Top + (int)(box.Y * area.Height),
            (int)(box.Width * area.Width), (int)(box.Height * area.Height), 0x0014))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (maximize) ShowWindow(window.Handle, 3);
    }
}
