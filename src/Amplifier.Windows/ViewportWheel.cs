using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Amplifier.Windows;

/// Installed only while the passive notice window is visible. Windows can route wheel
/// input to the foreground app even when our layered window owns the pointer pixel.
/// Intercept wheel only over our own unobscured viewport; never observe/log other input.
internal sealed class ViewportWheel : IDisposable
{
    private delegate nint Hook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public PointNative Point;public uint Data,Flags,Time;public nuint Extra; }
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int id, Hook callback,nint module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(PointNative point);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    private readonly Hook callback;
    private nint handle;
    public ViewportWheel(Window window, ScrollViewer viewport, Func<double> scale)
    {
        callback=(code,message,data)=>
        {
            if(code>=0 && message==(nint)0x20A && window.IsVisible && viewport.IsVisible)
            {
                var mouse=Marshal.PtrToStructure<MouseData>(data);
                if(WindowFromPoint(mouse.Point)==new WindowInteropHelper(window).Handle)
                {
                    var point=viewport.PointFromScreen(new Point(mouse.Point.X,mouse.Point.Y));
                    if(new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight).Contains(point))
                    {
                        var delta=unchecked((short)(mouse.Data>>16));
                        viewport.Dispatcher.BeginInvoke(()=>viewport.ScrollToVerticalOffset(viewport.VerticalOffset-delta*.5*scale()),DispatcherPriority.Input);
                        return 1;
                    }
                }
            }
            return CallNextHookEx(handle,code,message,data);
        };
        handle=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        if(handle==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not enable notification scrolling.");
    }
    public void Dispose() { if(handle!=0){UnhookWindowsHookEx(handle);handle=0;}GC.KeepAlive(callback); }
}
