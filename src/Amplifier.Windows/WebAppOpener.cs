using Amplifier.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Amplifier.Windows;

/// Windows' installed Edge PWA registration, not browser history/cookies/profile databases.
internal sealed class WebAppOpener
{
    private readonly Dictionary<string, (DateTime At, InstalledWebApp[] Apps)> cache = new();
    public async Task<bool> OpenAsync(Uri target)
    {
        var origin = target.GetLeftPart(UriPartial.Authority);
        if (!cache.TryGetValue(origin, out var entry) || DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(5))
        {
            entry = (DateTime.UtcNow, await DiscoverAsync(origin));cache[origin] = entry;
        }
        var matches = entry.Apps.Where(x => x.Accepts(target)).ToArray();
        // Multiple profile registrations need an explicit choice, not a guess.
        if (matches.Length != 1) return false;
        var executable = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", "", null) as string
            ?? Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", "", null) as string;
        if (executable == null || !File.Exists(executable)) return false;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in matches[0].Arguments(target)) start.ArgumentList.Add(argument);
        using var launched = Process.Start(start) ?? throw new IOException("The installed web app could not start.");
        return true;
    }
    private static async Task<InstalledWebApp[]> DiscoverAsync(string origin)
    {
        // Only matching origin metadata leaves the OS discovery process. No script interpolation
        // of URLs, shell invocation, credential handoff or private profile inspection.
        var encodedOrigin = Convert.ToBase64String(Encoding.UTF8.GetBytes(origin));
        var script = """
            $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
            $origin=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('ORIGIN_BASE64'))
            $result=@(Get-AppxPackage | ForEach-Object {
              try {
                $m=Get-AppxPackageManifest $_
                foreach($a in $m.Package.Applications.Application) {
                  foreach($e in $a.Extensions.Extension) {
                    $app=$e.AppExtension
                    if($app.Name -ne 'com.ms.webapp.internals.4') {continue}
                    $d=[string]$app.Description
                    if($d -notmatch 'start-url\?([^;]+)') {continue}
                    $url=$Matches[1]; $u=[Uri]$url
                    if($u.GetLeftPart([UriPartial]::Authority) -ne $origin) {continue}
                    if($d -notmatch '--app-id=([a-p]{32})(?:\s|;)') {continue}; $id=$Matches[1]
                    if($d -notmatch 'profile-directory\?([^;]+)') {continue}; $profile=$Matches[1]
                    if($d -notmatch '--ip-edge-aumid=Microsoft\.MicrosoftEdge\.Stable_') {continue}
                    @{startUrl=$url;appId=$id;profile=$profile}
                  }
                }
              } catch { }
            })
            ConvertTo-Json -InputObject $result -Compress
            """.Replace("ORIGIN_BASE64", encodedOrigin);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        if (process == null) return [];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var json = await output;await error;
            if (process.ExitCode != 0 || json.Length > 65536) return [];
            return JsonSerializer.Deserialize<InstalledWebApp[]>(json, Json.Options) ?? [];
        }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true);return []; }
        catch (JsonException) { return []; }
    }
}
