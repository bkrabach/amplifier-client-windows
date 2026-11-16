using Amplifier.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Amplifier.Windows;

public partial class App : Application
{
    private Mutex? singleton;
    private ClientController? controller;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test", StringComparer.Ordinal))
        {
            var results = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Amplifier for Windows Preview", "native-checks");
            try { NativeChecks.Run(results);Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(results);File.WriteAllText(Path.Combine(results, "failure.txt"), error.ToString());Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--interaction-test", StringComparer.Ordinal))
        {
            var results = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Amplifier for Windows Preview", "native-checks");
            try { await InteractionChecks.RunAsync(results);Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(results);File.WriteAllText(Path.Combine(results,"interaction-failure.txt"),error.ToString());Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--workspace-check")
        {
            try { var target = new Endpoint(e.Args[1]);Shutdown(await new WebAppOpener().OpenAsync(target.Origin) ? 0 : 2); }
            catch { Shutdown(1); }
            return;
        }
        var preview = e.Args.Contains("--preview", StringComparer.Ordinal);
        singleton = new Mutex(true, "Local\\Microsoft.Amplifier.Windows" + (preview ? ".Preview" : ""), out var first);
        if (!first) { Shutdown();return; }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", preview ? "Amplifier for Windows Preview" : "Amplifier for Windows");
            Directory.CreateDirectory(directory);
            controller = new ClientController(directory, preview);
            if (preview && !File.Exists(Path.Combine(directory, "preferences.json")))
            {
                controller.Preferences.Visibility = NoticeVisibility.Hover;
                controller.Preferences.Finish = "dark";
                controller.Save();
            }
            controller.Start();
        }
        catch (Exception error)
        {
            MessageBox.Show("Amplifier could not open its saved state. Existing files have been preserved.\n\n" + error.Message,
                "Amplifier", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { controller?.Dispose();singleton?.Dispose();base.OnExit(e); }
}
