using Amplifier.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Amplifier.Windows;

/// Off-screen WPF rendering and current-user protection. Never opens a service or another app.
internal static class NativeChecks
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        foreach (var shape in Presentation.Shapes)
        foreach (var finish in new[] { "clear", "dark" })
        {
            using var art = new AvatarVisual { Width = 320, Height = 320 };
            art.Load(shape, finish, "blue");
            art.Measure(new Size(320, 320));art.Arrange(new Rect(0, 0, 320, 320));
            byte[] Render(double time, string? file = null)
            {
                art.Show(AvatarState.Working, time);art.UpdateLayout();
                var bitmap = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32);bitmap.Render(art);
                var pixels = new byte[320 * 320 * 4];bitmap.CopyPixels(pixels, 320 * 4, 0);
                if (file != null) { var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream = File.Create(file);encoder.Save(stream); }
                return pixels;
            }
            var first = Render(.1, Path.Combine(directory, shape + "-" + finish + ".png"));
            var second = Render(.55);
            var alpha = Enumerable.Range(0, 320 * 320).Count(i => first[i * 4 + 3] > 0);
            var changed = !first.SequenceEqual(second);
            if (alpha < 100 || (!changed && SystemParameters.ClientAreaAnimation)) throw new InvalidDataException("Avatar rendering failed: " + shape + " " + finish);
            checks.Add(new { shape, finish, rendered = true, changed, visiblePixels = alpha });
        }
        var secret = System.Text.Encoding.UTF8.GetBytes("synthetic-credential-check");
        var protectedBytes = Native.Protect(secret, false);
        var restored = Native.Protect(protectedBytes, true);
        if (!secret.SequenceEqual(restored) || secret.SequenceEqual(protectedBytes)) throw new InvalidDataException("Windows credential protection failed.");
        CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(restored);
        File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, runtime = Environment.Version.ToString(), reducedMotion = !SystemParameters.ClientAreaAnimation, credentialRoundTrip = true, avatars = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
