using Amplifier.Core;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Amplifier.Windows;

/// Synthetic windows only; no host, registration, chat or user preference access.
internal static class InteractionChecks
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, int data, nuint extra);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
    public static async Task RunAsync(string evidence)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AmplifierInteraction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);GetCursorPos(out var original);var foreground = GetForegroundWindow();
        var behind = new Window { Title = "Amplifier synthetic wheel receiver", Width = 400, Height = 600,
            Left = 40, Top = 100, ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = Brushes.WhiteSmoke, Content = new TextBlock { Text = "Synthetic scrolling check" } };
        var leaked = 0;behind.PreviewMouseWheel += (_,e) => { leaked++;e.Handled=true; };
        ClientController? owner = null;
        try
        {
            behind.SourceInitialized += (_,_) => Native.Passive(behind);behind.Show();
            var prefs = new Preferences { Visibility = NoticeVisibility.Visible, Expanded = true, FitAll = false, MaximumHeight = 220, AvatarLeft = 240, AvatarTop = 500 };
            AtomicFile.Write(Path.Combine(directory,"preferences.json"),prefs);
            owner = new ClientController(directory, true);owner.Start();
            var runtime = owner.Services.Single();
            runtime.Observation = new("synthetic","instance",[],Enumerable.Range(0,16).Select(i=>new Notice("n"+i,"f","completion",new("session","s"+i),false,false,"Synthetic update "+i,"Synthetic detail",false,false)).ToArray(),new(0,0,0,16,0));
            owner.PreferencesChanged();await Task.Delay(500);
            var list = Application.Current.Windows.OfType<NoticeWindow>().Single();
            var avatar = Application.Current.Windows.OfType<TempoWindow>().Single();
            var scroll = ((Grid)list.Content).Children.OfType<ScrollViewer>().Single();
            var offsets = new System.Collections.Generic.List<double>();
            foreach(var size in Enum.GetValues<NoticeSize>())
            {
                owner.Preferences.NoticeSize=size;owner.PreferencesChanged();await Task.Delay(150);
                scroll.ScrollToTop();await Task.Delay(100);
                // Mid-gap between cards: not a button, border, header or opaque pixel.
                var point=scroll.PointToScreen(new Point(scroll.ActualWidth/2,66*NoticeSizes.Scale(size)));
                SetCursorPos((int)point.X,(int)point.Y);mouse_event(0x0800,0,0,-120,0);await Task.Delay(200);
                if(scroll.VerticalOffset<=0 || leaked!=0) throw new InvalidDataException($"Gap wheel escaped: {size}, offset={scroll.VerticalOffset}, leaked={leaked}, scrollable={scroll.ScrollableHeight}, visible={list.IsVisible}, point={point}, hit={WindowFromPoint(new POINT { X=(int)point.X,Y=(int)point.Y })}, list={new WindowInteropHelper(list).Handle}, size={scroll.ActualWidth}x{scroll.ActualHeight}");
                offsets.Add(scroll.VerticalOffset);
                if(list.Top<Native.WorkArea(list).Top || list.Top+list.Height>avatar.Top) throw new InvalidDataException("Notice anchor moved.");
            }
            var beforeOutside = scroll.VerticalOffset;
            var outside = list.PointToScreen(new Point(list.Width+25, list.Height/2));
            SetCursorPos((int)outside.X,(int)outside.Y);mouse_event(0x0800,0,0,-120,0);await Task.Delay(150);
            if(scroll.VerticalOffset!=beforeOutside) throw new InvalidDataException("Captured a wheel outside the viewport.");
            var oldOffset = scroll.VerticalOffset;
            runtime.Observation = runtime.Observation! with { Notices = new[]{new Notice("new","f","completion",new("session","new"),false,false,"New item","Synthetic detail",false,false)}.Concat(runtime.Observation.Notices).ToArray() };
            owner.PreferencesChanged();await Task.Delay(150);
            if(Math.Abs(scroll.VerticalOffset-oldOffset-66)>1) throw new InvalidDataException("Polling lost the visible row anchor.");
            var expanded=owner.Preferences.Expanded;var maximum=owner.Preferences.MaximumHeight;
            foreach(var label in new[]{"Hidden","On hover","Always visible"})
            {
                var item=avatar.ContextMenu.Items.OfType<MenuItem>().Single(x=>Equals(x.Header,label));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(50);
                if(owner.Preferences.Expanded!=expanded || owner.Preferences.MaximumHeight!=maximum) throw new InvalidDataException("Visibility lost presentation state.");
            }
            if(GetForegroundWindow()!=foreground) throw new InvalidDataException("Passive test stole foreground focus.");
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence,"interaction.json"),JsonSerializer.Serialize(new { passed=true, gapWheelOffsets=offsets, underlyingWheelEvents=leaked, foregroundPreserved=true, visibilityPreservesSize=true, outsideWheelNotCaptured=true, scrollAnchorPreserved=true }));
        }
        finally { owner?.Dispose();behind.Close();SetCursorPos(original.X,original.Y);Directory.Delete(directory,true); }
    }
}
