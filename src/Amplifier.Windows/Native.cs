using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Amplifier.Windows;

internal static class Native
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    public static Point Cursor(Window window)
    {
        GetCursorPos(out var point);var dpi = VisualTreeHelper.GetDpi(window);
        return new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }
    public static Rect WorkArea(Window window)
    {
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(area.X / dpi.DpiScaleX, area.Y / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }
    public static void Passive(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, (nint)((style | 0x08000000L | 0x80) & ~0x40000L));
        HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
        {
            if (message == 0x21) { handled = true;return (nint)3; } // WM_MOUSEACTIVATE: MA_NOACTIVATE
            return 0;
        });
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size;public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    public static byte[] Protect(byte[] data, bool decrypt = false)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = decrypt ? CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output) : CryptProtectData(ref input, "Amplifier service connection", 0, 0, 0, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];Marshal.Copy(output.Data, result, 0, result.Length);return result;
        }
        finally
        {
            for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) { for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);LocalFree(output.Data); }
        }
    }
}
