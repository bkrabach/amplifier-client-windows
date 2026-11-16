using System.Text.RegularExpressions;

namespace Amplifier.Core;

public sealed record InstalledWebApp(string StartUrl, string AppId, string Profile)
{
    public bool Accepts(Uri target) => Uri.TryCreate(StartUrl, UriKind.Absolute, out var start) &&
        start.Scheme == "https" && string.IsNullOrEmpty(start.UserInfo) &&
        start.GetLeftPart(UriPartial.Authority) == target.GetLeftPart(UriPartial.Authority) &&
        string.IsNullOrEmpty(target.UserInfo) && Regex.IsMatch(AppId, "^[a-p]{32}$") &&
        Regex.IsMatch(Profile, "^[A-Za-z0-9][A-Za-z0-9 _.-]{0,99}$") && Profile is not "." and not "..";
    public string[] Arguments(Uri target)
    {
        if (!Accepts(target)) throw new InvalidOperationException("The installed app belongs to a different service.");
        return ["--app-id=" + AppId, "--profile-directory=" + Profile,
            "--app-launch-url-for-shortcuts-menu-item=" + target.AbsoluteUri];
    }
}
