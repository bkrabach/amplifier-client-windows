using Amplifier.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Amplifier.Windows;

internal sealed class AvatarVisual : FrameworkElement, IDisposable
{
    private BitmapSource[] poses = [];
    private ZipArchive? animation;
    private double[] ends = [];
    private readonly Dictionary<int, BitmapSource> cache = new();
    private readonly Queue<int> cacheOrder = new();
    private string shape = "relay", palette = "blue", loaded = "";
    private AvatarState state = AvatarState.Offline;
    private double elapsed;
    public void Load(string name, string finish, string color)
    {
        var key = name + ":" + finish + ":" + color;if (key == loaded) return;
        shape = name;palette = color;animation?.Dispose();animation = null;cache.Clear();cacheOrder.Clear();ends = [];
        var resource = name + (finish == "dark" ? "-dark" : "");
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets");
        var bitmap = Read(File.OpenRead(Path.Combine(directory, resource + ".png")));
        poses = Enumerable.Range(0, AvatarPose.Count(name)).Select(i => Colorize(new CroppedBitmap(bitmap, new Int32Rect(i % 4 * 320, i / 4 * 320, 320, 320)))).ToArray();
        var path = Path.Combine(directory, resource + "-working.zip");
        if (File.Exists(path))
        {
            animation = ZipFile.OpenRead(path);
            using var metadata = JsonDocument.Parse(animation.GetEntry("animation.json")!.Open());
            double duration = 0;ends = metadata.RootElement.GetProperty("durations").EnumerateArray().Select(x => duration += x.GetDouble()).ToArray();
        }
        loaded = key;InvalidateVisual();
    }
    private static BitmapSource Read(Stream stream)
    {
        using (stream)
        using (var seekable = new MemoryStream())
        {
            // ZIP entry streams are not seekable; WPF can otherwise retain a deferred decoder.
            stream.CopyTo(seekable);seekable.Position = 0;
            var bitmap = BitmapFrame.Create(seekable, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmap.Freeze();return bitmap;
        }
    }
    private BitmapSource Frame(int index)
    {
        if (cache.TryGetValue(index, out var frame)) return frame;
        frame = Colorize(Read(animation!.GetEntry(index.ToString("D4") + ".png")!.Open()));
        cache[index] = frame;cacheOrder.Enqueue(index);
        while (cacheOrder.Count > 12) cache.Remove(cacheOrder.Dequeue());
        return frame;
    }
    private BitmapSource Colorize(BitmapSource source)
    {
        if (palette == "blue") { source.Freeze();return source; }
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;var pixels = new byte[stride * converted.PixelHeight];converted.CopyPixels(pixels, stride, 0);
        var hue = palette switch { "cyan" => .5, "amber" => .1, "crimson" => .97, "green" => .39, "purple" => .76, "cyan-amber" => .5, "purple-blue" => .76, _ => .6 };
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i] / 255.0;var g = pixels[i + 1] / 255.0;var r = pixels[i + 2] / 255.0;
            var max = Math.Max(r, Math.Max(g, b));var min = Math.Min(r, Math.Min(g, b));var saturation = max == 0 ? 0 : (max - min) / max;
            if (saturation < .01) continue;
            var h = hue;
            if (palette.Contains('-')) h += Math.Clamp((b - g) * 2, 0, 1) * (palette == "cyan-amber" ? -.4 : -.16);
            var c = max * saturation;var x = c * (1 - Math.Abs(h * 6 % 2 - 1));var m = max - c;
            (double rr, double gg, double bb) = (int)(h * 6) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
            var weight = Math.Min(1, saturation / .18);
            pixels[i] = (byte)Math.Clamp(255 * (b * (1 - weight) + (bb + m) * weight), 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(255 * (g * (1 - weight) + (gg + m) * weight), 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(255 * (r * (1 - weight) + (rr + m) * weight), 0, 255);
        }
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);result.Freeze();return result;
    }
    public void Show(AvatarState next, double time) { state = next;elapsed = time;InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if (poses.Length == 0) return;
        var reduced = !SystemParameters.ClientAreaAnimation;
        var pose = AvatarPose.Sample(shape, state, elapsed, reduced);
        dc.PushTransform(new TranslateTransform(0, -pose.Rise));
        dc.PushTransform(new RotateTransform(pose.Tilt, ActualWidth / 2, ActualHeight / 2));
        dc.PushTransform(new ScaleTransform(pose.ScaleX, pose.ScaleY, ActualWidth / 2, ActualHeight / 2));
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (state == AvatarState.Working && !reduced && animation != null && ends.Length > 0)
        {
            var time = elapsed % ends[^1];var index = Array.FindIndex(ends, x => x > time);dc.DrawImage(Frame(Math.Max(0, index)), rect);
        }
        else
        {
            // Opaque base plus weighted overlays prevents a mid-crossfade dimming pulse.
            var layers = pose.Weights.Select((w, i) => (w, i)).Where(x => x.w > 0).ToArray();
            double total = 0;
            foreach (var layer in layers) { total += layer.w;dc.PushOpacity(layer.w / total);dc.DrawImage(poses[layer.i], rect);dc.Pop(); }
        }
        dc.Pop();dc.Pop();dc.Pop();
    }
    public void Dispose() { animation?.Dispose();animation = null;cache.Clear();poses = []; }
}
